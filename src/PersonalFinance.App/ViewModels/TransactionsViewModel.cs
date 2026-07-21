using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PersonalFinance.App.Services;
using PersonalFinance.Shared.DTOs;
using System.Collections.ObjectModel;

namespace PersonalFinance.App.ViewModels;

public partial class TransactionsViewModel : ObservableObject
{
    private readonly ApiService _apiService;

    public TransactionsViewModel(ApiService apiService)
    {
        _apiService = apiService;
    }

    [ObservableProperty]
    private ObservableCollection<TransactionResponse> transactions = [];

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string errorMessage = string.Empty;

    [RelayCommand]
    public async Task LoadTransactionsAsync()
    {
        IsLoading = true;
        ErrorMessage = string.Empty;

        var result = await _apiService.GetTransactionsAsync();
        Transactions = new ObservableCollection<TransactionResponse>(result);

        IsLoading = false;
    }
}