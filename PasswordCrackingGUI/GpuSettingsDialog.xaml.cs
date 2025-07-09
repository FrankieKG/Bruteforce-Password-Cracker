using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PasswordCracking;

namespace PasswordCrackingGUI
{
    public partial class GpuSettingsDialog : Window
    {
        private List<GpuInfo> gpus;
        private List<GpuSettingsControl> settingsControls;
        
        public bool DialogResult { get; private set; } = false;
        
        public GpuSettingsDialog(List<GpuInfo> selectedGpus)
        {
            InitializeComponent();
            this.gpus = selectedGpus;
            this.settingsControls = new List<GpuSettingsControl>();
            
            SetupEventHandlers();
            PopulateGpuSettings();
        }
        
        private void SetupEventHandlers()
        {
            ApplyButton.Click += ApplyButton_Click;
            CancelButton.Click += CancelButton_Click;
            ResetButton.Click += ResetButton_Click;
        }
        
        private void PopulateGpuSettings()
        {
            GpuSettingsPanel.Children.Clear();
            settingsControls.Clear();
            
            for (int i = 0; i < gpus.Count; i++)
            {
                var gpu = gpus[i];
                var settingsControl = CreateGpuSettingsControl(gpu, i + 1);
                settingsControls.Add(settingsControl);
                GpuSettingsPanel.Children.Add(settingsControl.Container);
            }
        }
        
        private GpuSettingsControl CreateGpuSettingsControl(GpuInfo gpu, int gpuNumber)
        {
            var control = new GpuSettingsControl();
            
            // Create main container
            control.Container = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x4A, 0x4A, 0x4A)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 0, 0, 16)
            };
            
            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            
            // GPU Header
            var headerPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
            
            var gpuIcon = new TextBlock 
            { 
                Text = "🖥️", 
                FontSize = 16, 
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            
            var gpuTitle = new TextBlock
            {
                Text = $"GPU {gpuNumber}: {gpu.Name}",
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold,
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center
            };
            
            var gpuSpecs = new TextBlock
            {
                Text = $"Memory: {gpu.MemoryBytes / KernelConstants.BYTES_PER_GB} GB • Compute Units: {gpu.ComputeUnits}",
                Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA)),
                FontSize = 11,
                Margin = new Thickness(12, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            
            headerPanel.Children.Add(gpuIcon);
            headerPanel.Children.Add(gpuTitle);
            headerPanel.Children.Add(gpuSpecs);
            Grid.SetRow(headerPanel, 0);
            
            // Settings Grid
            var settingsGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            settingsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            settingsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            settingsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            
            // Batch Size
            var batchLabel = new TextBlock
            {
                Text = "Batch Size:",
                Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            Grid.SetColumn(batchLabel, 0);
            
            control.BatchSizeTextBox = new TextBox
            {
                Style = (Style)FindResource("ModernTextBox"),
                Text = gpu.BatchSize.ToString(),
                Width = 120,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 16, 0)
            };
            Grid.SetColumn(control.BatchSizeTextBox, 0);
            
            // Memory Limit
            var memoryLabel = new TextBlock
            {
                Text = "Memory Limit (%):",
                Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            Grid.SetColumn(memoryLabel, 1);
            
            control.MemoryLimitTextBox = new TextBox
            {
                Style = (Style)FindResource("ModernTextBox"),
                Text = gpu.MemoryLimitPercent.ToString(),
                Width = 80,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 16, 0)
            };
            Grid.SetColumn(control.MemoryLimitTextBox, 1);
            
            // Reset button for this GPU
            var resetGpuButton = new Button
            {
                Content = "🔄",
                Style = (Style)FindResource("ModernButton"),
                Background = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
                Width = 30,
                Height = 30,
                Margin = new Thickness(8, 0, 0, 0),
                ToolTip = "Reset this GPU to defaults"
            };
            resetGpuButton.Click += (s, e) => ResetGpuToDefaults(control);
            Grid.SetColumn(resetGpuButton, 2);
            
            // Create layout for batch size
            var batchPanel = new StackPanel { Orientation = Orientation.Vertical };
            batchPanel.Children.Add(batchLabel);
            batchPanel.Children.Add(control.BatchSizeTextBox);
            Grid.SetColumn(batchPanel, 0);
            
            // Create layout for memory limit
            var memoryPanel = new StackPanel { Orientation = Orientation.Vertical };
            memoryPanel.Children.Add(memoryLabel);
            memoryPanel.Children.Add(control.MemoryLimitTextBox);
            Grid.SetColumn(memoryPanel, 1);
            
            settingsGrid.Children.Add(batchPanel);
            settingsGrid.Children.Add(memoryPanel);
            settingsGrid.Children.Add(resetGpuButton);
            Grid.SetRow(settingsGrid, 1);
            
            // Recommendations
            var recommendationsPanel = new StackPanel { Orientation = Orientation.Vertical };
            var recommendationText = new TextBlock
            {
                Text = GetRecommendationsText(gpu),
                Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                FontSize = 10,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0)
            };
            recommendationsPanel.Children.Add(recommendationText);
            Grid.SetRow(recommendationsPanel, 2);
            
            mainGrid.Children.Add(headerPanel);
            mainGrid.Children.Add(settingsGrid);
            mainGrid.Children.Add(recommendationsPanel);
            
            control.Container.Child = mainGrid;
            control.Gpu = gpu;
            
            return control;
        }
        
        private string GetRecommendationsText(GpuInfo gpu)
        {
            var memoryGB = gpu.MemoryBytes / KernelConstants.BYTES_PER_GB;
            var recommendations = new List<string>();
            
            // Batch size recommendations
            if (memoryGB >= 8)
                recommendations.Add("High memory GPU: Try batch sizes 2M-10M");
            else if (memoryGB >= 4)
                recommendations.Add("Medium memory GPU: Try batch sizes 500K-2M");
            else
                recommendations.Add("Low memory GPU: Try batch sizes 100K-500K");
            
            // Memory limit recommendations
            recommendations.Add("Memory limit 70-90% recommended (80% is safe default)");
            
            return string.Join(" • ", recommendations);
        }
        
        private void ResetGpuToDefaults(GpuSettingsControl control)
        {
            control.BatchSizeTextBox.Text = "1000000";
            control.MemoryLimitTextBox.Text = "80";
        }
        
        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            // Validate and apply settings
            if (ValidateAndApplySettings())
            {
                DialogResult = true;
                Close();
            }
        }
        
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
        
        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (var control in settingsControls)
            {
                ResetGpuToDefaults(control);
            }
        }
        
        private bool ValidateAndApplySettings()
        {
            for (int i = 0; i < settingsControls.Count; i++)
            {
                var control = settingsControls[i];
                var gpu = gpus[i];
                
                // Validate batch size
                if (!ulong.TryParse(control.BatchSizeTextBox.Text.Trim(), out ulong batchSize) || batchSize == 0)
                {
                    MessageBox.Show($"Invalid batch size for {gpu.Name}. Please enter a positive number.", 
                                  "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    control.BatchSizeTextBox.Focus();
                    return false;
                }
                
                if (batchSize > 100000000) // 100M limit
                {
                    MessageBox.Show($"Batch size too large for {gpu.Name}. Maximum is 100,000,000.", 
                                  "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    control.BatchSizeTextBox.Focus();
                    return false;
                }
                
                // Validate memory limit
                if (!int.TryParse(control.MemoryLimitTextBox.Text.Trim(), out int memoryLimit) || 
                    memoryLimit <= 0 || memoryLimit > 100)
                {
                    MessageBox.Show($"Invalid memory limit for {gpu.Name}. Please enter a number between 1-100.", 
                                  "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    control.MemoryLimitTextBox.Focus();
                    return false;
                }
                
                // Apply settings
                gpu.BatchSize = batchSize;
                gpu.MemoryLimitPercent = memoryLimit;
            }
            
            return true;
        }
    }
    
    // Helper class to manage GPU setting controls
    public class GpuSettingsControl
    {
        public Border Container { get; set; }
        public TextBox BatchSizeTextBox { get; set; }
        public TextBox MemoryLimitTextBox { get; set; }
        public GpuInfo Gpu { get; set; }
    }
} 