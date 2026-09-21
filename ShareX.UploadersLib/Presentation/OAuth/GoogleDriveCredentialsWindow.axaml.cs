#region License Information (GPL v3)

/*
    ShareX - A program that allows you to take screenshots and share any file type
    Copyright (c) 2007-2026 ShareX Team
*/

#endregion License Information (GPL v3)

#nullable enable

using Avalonia.Controls;
using Avalonia.Interactivity;
using ShareX.AvaloniaUI.Theming;
using ShareX.HelpersLib;
using System;

namespace ShareX.UploadersLib;

public partial class GoogleDriveCredentialsWindow : Window
{
    public bool Succeeded { get; private set; }
    public string ClientId { get; private set; } = string.Empty;
    public string ClientSecret { get; private set; } = string.Empty;

    public GoogleDriveCredentialsWindow() : this(string.Empty, string.Empty)
    {
    }

    public GoogleDriveCredentialsWindow(string defaultClientId, string defaultClientSecret)
    {
        InitializeComponent();
        RequestedThemeVariant = ThemeManager.GetCurrentTheme();

        ClientIdTextBox.Text = defaultClientId;
        ClientSecretTextBox.Text = defaultClientSecret;
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        string id = (ClientIdTextBox.Text ?? string.Empty).Trim();
        string secret = (ClientSecretTextBox.Text ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(id))
        {
            StatusText.Text = "Client ID is required.";
            StatusText.IsVisible = true;
            return;
        }

        ClientId = id;
        ClientSecret = secret;
        Succeeded = true;
        Close();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnConsoleLinkClick(object? sender, RoutedEventArgs e)
    {
        URLHelpers.OpenURL("https://console.cloud.google.com/apis/credentials");
    }
}
