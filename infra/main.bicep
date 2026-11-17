// Personal Finance API on Azure Container Apps + Azure Database for
// PostgreSQL Flexible Server (#123). Deploying this costs money - see
// docs/DEPLOYMENT.md for the runbook and the cost notes. Nothing here runs
// until someone runs `az deployment group create`.
//
//   az deployment group create -g <rg> -f infra/main.bicep -p infra/main.bicepparam

targetScope = 'resourceGroup'

@description('Prefix for resource names, e.g. "pfapp". Lowercase letters and digits.')
@minLength(3)
@maxLength(12)
param namePrefix string

param location string = resourceGroup().location

@description('API image, e.g. ghcr.io/<owner>/personalfinance-api:0.3.0')
param containerImage string

@description('Postgres admin (schema owner) login - used only by the migrate job.')
param postgresAdminLogin string = 'pfadmin'

@secure()
param postgresAdminPassword string

@secure()
@description('Password the migrate job gives pf_app, the role the API connects as.')
param appRolePassword string

@secure()
@minLength(32)
param jwtKey string

@secure()
param plaidClientId string

@secure()
param plaidSecret string

@allowed(['sandbox', 'production'])
param plaidEnvironment string = 'sandbox'

@description('Key-encryption key id, e.g. k1 (docs/KEY_MANAGEMENT.md).')
param encryptionKeyId string = 'k1'

@secure()
@description('Key-encryption key: base64 of 32 random bytes (scripts/new-kek.sh).')
param encryptionKey string

@description('OTLP endpoint for traces/logs/metrics; empty to export nothing (logs still reach Log Analytics via stdout).')
param otlpEndpoint string = ''

var databaseName = 'personalfinance'
var postgresHost = '${postgres.name}.postgres.database.azure.com'
// VerifyFull: TLS required and the server certificate checked against the
// hostname (Azure's certificates chain to a public CA the image trusts).
var connectionBase = 'Host=${postgresHost};Database=${databaseName};SSL Mode=VerifyFull'

// ── Logs ──────────────────────────────────────────────────────────────────────
resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${namePrefix}-logs'
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

// ── Database ──────────────────────────────────────────────────────────────────
resource postgres 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' = {
  name: '${namePrefix}-pg'
  location: location
  sku: {
    name: 'Standard_B1ms' // burstable: the cheapest tier, plenty for a demo
    tier: 'Burstable'
  }
  properties: {
    version: '16'
    administratorLogin: postgresAdminLogin
    administratorLoginPassword: postgresAdminPassword
    storage: { storageSizeGB: 32 }
    backup: {
      backupRetentionDays: 7
      geoRedundantBackup: 'Disabled'
    }
    highAvailability: { mode: 'Disabled' }
    network: { publicNetworkAccess: 'Enabled' }
  }

  resource database 'databases' = {
    name: databaseName
  }

  // Container Apps' outbound addresses aren't fixed, so allow Azure-internal
  // traffic (the special 0.0.0.0 rule) rather than the internet. Private
  // networking (VNet integration) is the upgrade - see docs/DEPLOYMENT.md.
  resource allowAzure 'firewallRules' = {
    name: 'AllowAzureServices'
    properties: {
      startIpAddress: '0.0.0.0'
      endIpAddress: '0.0.0.0'
    }
  }

  // TLS 1.2+ only (require_secure_transport is on by default).
  resource minTls 'configurations' = {
    name: 'ssl_min_protocol_version'
    properties: {
      value: 'TLSv1.2'
      source: 'user-override'
    }
  }
}

// ── Container Apps ────────────────────────────────────────────────────────────
resource environment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: '${namePrefix}-env'
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logs.properties.customerId
        sharedKey: logs.listKeys().primarySharedKey
      }
    }
  }
}

// Secrets live in Container Apps' secret store (encrypted by the platform),
// referenced by name from env vars - never baked into the image.
var appSecrets = [
  { name: 'db-app', value: '${connectionBase};Username=pf_app;Password=${appRolePassword}' }
  { name: 'jwt-key', value: jwtKey }
  { name: 'plaid-client-id', value: plaidClientId }
  { name: 'plaid-secret', value: plaidSecret }
  { name: 'kek', value: encryptionKey }
]

var sharedEnv = [
  { name: 'Jwt__Key', secretRef: 'jwt-key' }
  { name: 'Jwt__Issuer', value: 'PersonalFinance.Api' }
  { name: 'Jwt__Audience', value: 'PersonalFinance.Api' }
  { name: 'Plaid__ClientId', secretRef: 'plaid-client-id' }
  { name: 'Plaid__Secret', secretRef: 'plaid-secret' }
  { name: 'Plaid__Environment', value: plaidEnvironment }
  { name: 'Encryption__CurrentKeyId', value: encryptionKeyId }
  { name: 'Encryption__Keys__${encryptionKeyId}', secretRef: 'kek' }
]

resource api 'Microsoft.App/containerApps@2024-03-01' = {
  name: '${namePrefix}-api'
  location: location
  properties: {
    managedEnvironmentId: environment.id
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
        // HTTPS only: plain HTTP is redirected at the ingress, which
        // terminates TLS with a managed certificate for the app's FQDN.
        allowInsecure: false
      }
      secrets: appSecrets
    }
    template: {
      containers: [
        {
          name: 'api'
          image: containerImage
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: concat(sharedEnv, [
            { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
            { name: 'ConnectionStrings__Default', secretRef: 'db-app' }
            // The ingress is the only way in, so its X-Forwarded-* are trustworthy.
            { name: 'ReverseProxy__Enabled', value: 'true' }
            { name: 'Plaid__WebhookUrl', value: 'https://${namePrefix}-api.${environment.properties.defaultDomain}/api/v1/plaid/webhook' }
            { name: 'OTEL_EXPORTER_OTLP_ENDPOINT', value: otlpEndpoint }
          ])
          probes: [
            {
              type: 'Liveness'
              httpGet: { path: '/health/live', port: 8080 }
              periodSeconds: 15
            }
            {
              type: 'Readiness'
              httpGet: { path: '/health/ready', port: 8080 }
              periodSeconds: 10
            }
          ]
        }
      ]
      scale: {
        // Exactly one: the scheduled Plaid sync and the webhook worker run
        // in-process, so scaling to zero would stop them (and webhooks
        // would hit a cold start). More replicas are safe (#114's row
        // lock), just each runs its own schedule.
        minReplicas: 1
        maxReplicas: 1
      }
    }
  }
}

// One-off: migrations as the schema owner, then pf_app's password. Run it
// before the first revision and after every release with migrations:
//   az containerapp job start -g <rg> -n <prefix>-migrate
resource migrate 'Microsoft.App/jobs@2024-03-01' = {
  name: '${namePrefix}-migrate'
  location: location
  properties: {
    environmentId: environment.id
    configuration: {
      triggerType: 'Manual'
      replicaTimeout: 600
      replicaRetryLimit: 1
      manualTriggerConfig: {
        parallelism: 1
        replicaCompletionCount: 1
      }
      secrets: concat(appSecrets, [
        { name: 'db-owner', value: '${connectionBase};Username=${postgresAdminLogin};Password=${postgresAdminPassword}' }
        { name: 'app-role-password', value: appRolePassword }
      ])
    }
    template: {
      containers: [
        {
          name: 'migrate'
          image: containerImage
          args: ['migrate']
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
          env: concat(sharedEnv, [
            { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
            { name: 'ConnectionStrings__Migrations', secretRef: 'db-owner' }
            { name: 'Database__AppRolePassword', secretRef: 'app-role-password' }
          ])
        }
      ]
    }
  }
}

output apiUrl string = 'https://${api.properties.configuration.ingress.fqdn}'
output migrateJobName string = migrate.name
output postgresHost string = postgresHost
