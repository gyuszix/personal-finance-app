#!/usr/bin/env bash
# Prints a new random 256-bit key-encryption key (base64) for
# Encryption:Keys:<id>. See docs/KEY_MANAGEMENT.md. Nothing is stored.
set -euo pipefail
openssl rand -base64 32
