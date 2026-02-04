using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using DeskFolder.Services;
using System;
using System.Collections.Generic;
using System.Linq; // Added for Enumerable methods
using System.Threading.Tasks; // Added for async Tasks

namespace DeskFolder.Views
{
    public partial class BackupManagerWindow : Window
    {
        private BackupService _backupService;
        private ListBox? _backupsList;
        public bool WasRestoreSuccessful { get; private set; } = false;

        public BackupManagerWindow()
        {
            InitializeComponent();
#if DEBUG
            this.AttachDevTools();
#endif
            _backupService = new BackupService();
            _backupsList = this.FindControl<ListBox>("BackupsList");
            LoadBackups();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void LoadBackups()
        {
            if (_backupsList != null)
            {
                _backupsList.ItemsSource = _backupService.GetBackups();
            }
        }

        private async void CreateBackupButton_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            var progressBar = this.FindControl<ProgressBar>("BackupProgressBar");
            var statusText = this.FindControl<TextBlock>("StatusText");
            
            if (btn != null) btn.IsEnabled = false;
            if (progressBar != null) progressBar.IsVisible = true;
            if (statusText != null) 
            {
                statusText.IsVisible = true;
                statusText.Text = "Starting backup...";
                statusText.Foreground = Avalonia.Media.Brushes.Gray;
            }

            var progress = new Progress<string>(status => 
            {
                if (statusText != null) statusText.Text = status;
            });

            try
            {
                await _backupService.CreateBackupAsync(progress);
                LoadBackups();
                
                if (statusText != null)
                {
                    statusText.Text = "Backup created successfully";
                    statusText.Foreground = Avalonia.Media.Brushes.LightGreen;
                    statusText.IsVisible = true;
                }
            }
            catch (Exception ex)
            {
                if (statusText != null)
                {
                    statusText.Text = $"Error: {ex.Message}";
                    statusText.Foreground = Avalonia.Media.Brushes.IndianRed;
                    statusText.IsVisible = true;
                    // Log to debug too
                     System.Diagnostics.Debug.WriteLine(ex);
                }
            }
            finally
            {
                if (btn != null) btn.IsEnabled = true;
                 if (progressBar != null) progressBar.IsVisible = false;
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private async void RestoreItem_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            var item = btn?.Tag as BackupItem;
            if (item == null) return;

            var progressBar = this.FindControl<ProgressBar>("BackupProgressBar");
            var statusText = this.FindControl<TextBlock>("StatusText");

            // Disable UI
            this.IsEnabled = false;
            if (progressBar != null) progressBar.IsVisible = true;
            if (statusText != null) 
            {
                statusText.IsVisible = true;
                statusText.Text = "Starting restore...";
                statusText.Foreground = Avalonia.Media.Brushes.Gray;
            }

            var progress = new Progress<string>(status => 
            {
                if (statusText != null) statusText.Text = status;
            });
            
            try
            {
                await _backupService.RestoreBackupAsync(item.FilePath, progress);

                if (statusText != null)
                {
                    statusText.Text = "Restored successfully! Restarting app...";
                    statusText.Foreground = Avalonia.Media.Brushes.LightGreen;
                }
                
                // Small delay to read message
                await Task.Delay(1500);

                WasRestoreSuccessful = true;
                Close();
            }
            catch (Exception ex)
            {
                this.IsEnabled = true; // Re-enable interaction on error
                if (progressBar != null) progressBar.IsVisible = false;
                if (statusText != null)
                {
                    statusText.Text = $"Restore failed: {ex.Message}";
                    statusText.Foreground = Avalonia.Media.Brushes.IndianRed;
                }
                System.Diagnostics.Debug.WriteLine("Restore failed: " + ex);
            }
        }

        private void DeleteItem_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            var item = btn?.Tag as BackupItem;
            if (item == null) return;

            try
            {
                _backupService.DeleteBackup(item.FilePath);
                LoadBackups();
            }
            catch (Exception ex)
            {
                 System.Diagnostics.Debug.WriteLine("Delete failed: " + ex);
            }
        }
    }
}
