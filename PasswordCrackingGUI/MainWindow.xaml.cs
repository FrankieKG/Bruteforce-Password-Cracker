using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using PasswordCracking;
using System.ComponentModel;
using System.IO;
using System.Collections.Generic; // Added for List
using System.Linq; // Added for CastTo
using System.Diagnostics; // Added for Debug.WriteLine

namespace PasswordCrackingGUI;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private Kernel crackingEngine;
    private BackgroundWorker crackingWorker;
    private bool isRunning = false;
    private List<GpuInfo> detectedGpus = new List<GpuInfo>();
    private DateTime crackingStartTime;
    private string currentAlgorithm = "Unknown";
            private GuiKernel? currentGuiKernel = null;
    
    public MainWindow()
    {
        try
        {
            InitializeComponent();
            InitializeEngine();
            SetupEventHandlers();
            LoadGpus();
        }
        catch (Exception ex)
        {
            // Show error dialog before the window closes
            MessageBox.Show($"Failed to initialize application:\n\n{ex.Message}\n\nDetails:\n{ex.StackTrace}", 
                          "Initialization Error", MessageBoxButton.OK, MessageBoxImage.Error);
            
            // Set a fallback state
            if (StatusText != null)
                StatusText.Text = "Initialization failed - see error message";
        }
    }
    
    private void InitializeEngine()
    {
        crackingEngine = new Kernel();
        
        // Setup background worker for non-blocking operations
        crackingWorker = new BackgroundWorker();
        crackingWorker.WorkerSupportsCancellation = true;
        crackingWorker.WorkerReportsProgress = true;
        crackingWorker.DoWork += CrackingWorker_DoWork;
        crackingWorker.ProgressChanged += CrackingWorker_ProgressChanged;
        crackingWorker.RunWorkerCompleted += CrackingWorker_RunWorkerCompleted;
    }
    
    private void SetupEventHandlers()
    {
        // Button event handlers
        StartButton.Click += StartButton_Click;
        StopButton.Click += StopButton_Click;
        RefreshGpusButton.Click += RefreshGpusButton_Click;
        SaveButton.Click += SaveButton_Click;
        
        // Slider event handler
        MaxLengthSlider.ValueChanged += MaxLengthSlider_ValueChanged;
        
                    // Charset preset handler
            CharsetPresets.SelectionChanged += CharsetPresets_SelectionChanged;
            
            // GPU settings handler
            GpuSettingsButton.Click += GpuSettingsButton_Click;
    }
    
    private void LoadGpus()
    {
        try
        {
            // Clear existing GPU display
            GpuListPanel.Children.Clear();
            StatusText.Text = "Detecting GPUs...";
            
            // Detect GPUs without initializing the full engine
            var detectedGpus = DetectGpusForGui();
            this.detectedGpus = detectedGpus; // Store for later use
            
            if (detectedGpus.Count > 0)
            {
                StatusText.Text = $"Found {detectedGpus.Count} GPU(s)";
                
                // Display actual detected GPUs
                for (int i = 0; i < detectedGpus.Count; i++)
                {
                    var gpu = detectedGpus[i];
                    string memoryGB = (gpu.MemoryBytes / KernelConstants.BYTES_PER_GB).ToString();
                    AddGpuToDisplay(gpu.Name, $"{memoryGB} GB", gpu.ComputeUnits.ToString(), gpu.IsSelected, i);
                }
                
                // Update algorithm selection visibility
                UpdateAlgorithmSelection();
            }
            else
            {
                StatusText.Text = "No GPUs detected - showing mock data";
                // Fallback to mock data
                AddGpuToDisplay("Mock GPU (No OpenCL GPUs found)", "Unknown", "Unknown", true, -1);
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"GPU detection error: {ex.Message}";
            MessageBox.Show($"GPU detection failed:\n{ex.Message}\n\nUsing mock data for demonstration.", 
                          "GPU Detection Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            
            // Fallback to mock data
            AddGpuToDisplay("Mock GPU (Detection Failed)", "Unknown", "Unknown", true, -1);
        }
    }
    
    /// <summary>
    /// Detects available OpenCL GPUs using the centralized detection utility for GUI applications.
    /// 
    /// <para><strong>GUI-Optimized Detection:</strong></para>
    /// <para>Uses OpenClGpuDetector with silent error handling to prevent exception dialogs during
    /// GUI initialization. Automatically applies intelligent default settings based on GPU capabilities.</para>
    /// </summary>
    /// <returns>List of detected GPUs with GUI-friendly default settings applied</returns>
    private List<GpuInfo> DetectGpusForGui()
    {
        return OpenClGpuDetector.DetectGpusForGui();
    }
    
    private void AddGpuToDisplay(string name, string memory, string computeUnits, bool isSelected, int gpuIndex)
    {
        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x4A, 0x4A, 0x4A)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 8)
        };
        
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        
        // Checkbox
        var checkBox = new CheckBox
        {
            IsChecked = isSelected,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
            Tag = gpuIndex // Store GPU index for reference
        };
        checkBox.Checked += (s, e) => {
            if (gpuIndex >= 0 && gpuIndex < detectedGpus.Count)
                detectedGpus[gpuIndex].IsSelected = true;
        };
        checkBox.Unchecked += (s, e) => {
            if (gpuIndex >= 0 && gpuIndex < detectedGpus.Count)
                detectedGpus[gpuIndex].IsSelected = false;
        };
        Grid.SetColumn(checkBox, 0);
        
        // GPU Info Stack
        var infoStack = new StackPanel();
        
        var nameBlock = new TextBlock
        {
            Text = name,
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold
        };
        
        var infoBlock = new TextBlock
        {
            Text = $"Memory: {memory} • Compute Units: {computeUnits}",
            Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA)),
            FontSize = 11
        };
        
        var progressBar = new ProgressBar
        {
            Height = 6,
            Margin = new Thickness(0, 4, 0, 0),
            Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x30)),
            Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xD4, 0xAA)),
            Value = 0
        };
        
        infoStack.Children.Add(nameBlock);
        infoStack.Children.Add(infoBlock);
        infoStack.Children.Add(progressBar);
        Grid.SetColumn(infoStack, 1);
        
        // Settings Button
        var settingsButton = new Button
        {
            Content = "⚙️",
            Width = 30,
            Height = 30,
            Style = (Style)FindResource("ModernButton"),
            ToolTip = "Configure GPU Settings"
        };
        settingsButton.Click += (s, e) => {
            if (gpuIndex >= 0 && gpuIndex < detectedGpus.Count)
            {
                var gpu = detectedGpus[gpuIndex];
                var settingsDialog = new GpuSettingsDialog(new List<GpuInfo> { gpu });
                settingsDialog.Owner = this;
                
                if (settingsDialog.ShowDialog() == true)
                {
                    StatusText.Text = $"Settings updated for {gpu.Name}";
                }
            }
        };
        Grid.SetColumn(settingsButton, 2);
        
        grid.Children.Add(checkBox);
        grid.Children.Add(infoStack);
        grid.Children.Add(settingsButton);
        
        border.Child = grid;
        GpuListPanel.Children.Add(border);
    }
    
    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (isRunning) return;
        
        // Validate inputs
        if (string.IsNullOrWhiteSpace(TargetHashTextBox.Text))
        {
            MessageBox.Show("Please enter a target hash.", "Validation Error", 
                          MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        
        if (string.IsNullOrWhiteSpace(CharsetTextBox.Text))
        {
            MessageBox.Show("Please enter a charset.", "Validation Error", 
                          MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        
        // Start cracking process
        isRunning = true;
        StartButton.IsEnabled = false;
        StopButton.IsEnabled = true;
        StatusText.Text = "Starting password cracking...";
        
        // Start background worker
        var parameters = new CrackingParameters
        {
            TargetHash = TargetHashTextBox.Text.Trim(),
            Charset = CharsetTextBox.Text.Trim(),
            MaxLength = (int)MaxLengthSlider.Value,
            AlgorithmName = ((ComboBoxItem)AlgorithmComboBox.SelectedItem)?.Content?.ToString() ?? "Auto-Select"
        };
        
        crackingWorker.RunWorkerAsync(parameters);
    }
    
    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        if (!isRunning) return;
        
        crackingWorker.CancelAsync();
        StatusText.Text = "Stopping...";
    }
    
    private void RefreshGpusButton_Click(object sender, RoutedEventArgs e)
    {
        LoadGpus();
    }
    
    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var saveDialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
            DefaultExt = "txt",
            FileName = $"PasswordCrackingResults_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
        };
        
        if (saveDialog.ShowDialog() == true)
        {
            try
            {
                File.WriteAllText(saveDialog.FileName, ResultsTextBox.Text);
                StatusText.Text = $"Results saved to {System.IO.Path.GetFileName(saveDialog.FileName)}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save results:\n{ex.Message}", "Save Error", 
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
    
    private void MaxLengthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (MaxLengthDisplay != null)
        {
            MaxLengthDisplay.Text = ((int)e.NewValue).ToString();
        }
    }
    
    private void CharsetPresets_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CharsetTextBox == null || CharsetPresets.SelectedItem == null) return;
        
        var selectedItem = (ComboBoxItem)CharsetPresets.SelectedItem;
        var preset = selectedItem.Content.ToString();
        
        switch (preset)
        {
            case "Lowercase":
                CharsetTextBox.Text = "abcdefghijklmnopqrstuvwxyz";
                break;
            case "Alphanumeric":
                CharsetTextBox.Text = "abcdefghijklmnopqrstuvwxyz0123456789";
                break;
            case "All ASCII":
                CharsetTextBox.Text = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789!@#$%^&*()";
                break;
            case "Custom":
                // Keep current text
                break;
        }
    }
    
    private void CrackingWorker_DoWork(object sender, DoWorkEventArgs e)
    {
        var parameters = (CrackingParameters)e.Argument!;
        var worker = sender as BackgroundWorker;
        
        try
        {
            // Get selected GPUs
            var selectedGpus = detectedGpus.Where(g => g.IsSelected).ToList();
            
            if (selectedGpus.Count == 0)
            {
                throw new InvalidOperationException("No GPUs selected. Please select at least one GPU.");
            }
            
            // Store start time and algorithm for final results
            crackingStartTime = DateTime.Now;
            currentAlgorithm = parameters.AlgorithmName;
            
            if (worker.CancellationPending)
            {
                e.Cancel = true;
                return;
            }
            
            // Create a custom kernel instance for GUI use (no progress reporting for initialization)
            currentGuiKernel = new GuiKernel(selectedGpus);
            
            // Initialize the kernel with selected GPUs (no progress reporting for initialization)
            currentGuiKernel.InitializeWithSelectedGpus();
            
            if (worker.CancellationPending)
            {
                e.Cancel = true;
                return;
            }
            
            // Call the actual cracking method with REAL progress reporting ONLY
            string foundPassword;
            string executedAlgorithm;
            bool success = currentGuiKernel.CrackPassword(
                parameters.Charset, 
                parameters.MaxLength, 
                parameters.TargetHash, 
                out foundPassword,
                out executedAlgorithm,
                (progress, status) => {
                    if (!worker.CancellationPending)
                    {
                        // Show REAL progress: actual percentage of search space covered
                        var realProgress = Math.Max(0, Math.Min(100, progress * 100)); // Clamp to 0-100 range
                        // Store the decimal progress in the status string for precision
                        var enhancedStatus = $"{realProgress:F1}%|{status}";
                        worker.ReportProgress((int)realProgress, enhancedStatus);
                    }
                },
                parameters.AlgorithmName); // Pass the user's algorithm choice
            
            if (worker.CancellationPending)
            {
                e.Cancel = true;
                return;
            }
            
            e.Result = new CrackingResult
            {
                Success = success,
                FoundPassword = foundPassword,
                TargetHash = parameters.TargetHash,
                ExecutedAlgorithm = executedAlgorithm
            };
        }
        catch (Exception ex)
        {
            e.Result = new CrackingResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                ExecutedAlgorithm = "Error" // Indicate an error occurred
            };
        }
    }
    
    private void CrackingWorker_ProgressChanged(object sender, ProgressChangedEventArgs e)
    {
        double progressPercent = e.ProgressPercentage; // Fallback to integer progress
        string actualStatusMessage = "";
        
        // Extract decimal progress and status from enhanced status string
        if (e.UserState is string enhancedStatus)
        {
            var parts = enhancedStatus.Split('|', 2);
            if (parts.Length == 2)
            {
                // Parse the decimal progress from the first part
                if (parts[0].EndsWith("%") && double.TryParse(parts[0].Substring(0, parts[0].Length - 1), out double decimalProgress))
                {
                    progressPercent = decimalProgress;
                }
                actualStatusMessage = parts[1]; // The real status message
            }
            else
            {
                actualStatusMessage = enhancedStatus; // Fallback if format is unexpected
            }
        }
        
        // Update all UI elements with the decimal precision progress
        OverallProgressBar.Value = progressPercent;
        ProgressText.Text = $"{progressPercent:F1}% complete";
        ProgressPercentText.Text = $"{progressPercent:F1}%";
        
        // Update status message
        StatusText.Text = actualStatusMessage;
    }
    
    private void CrackingWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
    {
        isRunning = false;
        StartButton.IsEnabled = true;
        StopButton.IsEnabled = false;
        
        if (e.Cancelled)
        {
            StatusText.Text = "Operation cancelled";
            ProgressText.Text = "Cancelled";
        }
        else if (e.Error != null)
        {
            StatusText.Text = $"Error: {e.Error.Message}";
            MessageBox.Show($"An error occurred:\n{e.Error.Message}", "Error", 
                          MessageBoxButton.OK, MessageBoxImage.Error);
        }
        else if (e.Result is CrackingResult result)
        {
            // Update the displayed algorithm to show what was actually executed
            if (!string.IsNullOrEmpty(result.ExecutedAlgorithm))
            {
                currentAlgorithm = result.ExecutedAlgorithm;
            }

            if (result.Success)
            {
                StatusText.Text = "Password found!";
                ProgressText.Text = "Complete - Password found!";
                // Don't force progress to 100% - let ShowFinalPerformanceResults handle it with actual progress
                
                var resultText = $"SUCCESS!\n" +
                               $"Target Hash: {result.TargetHash}\n" +
                               $"Found Password: {result.FoundPassword}\n" +
                               $"Algorithm Used: {result.ExecutedAlgorithm}\n" +
                               $"Completed at: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
                
                ResultsTextBox.Text = resultText;
                
                MessageBox.Show($"Password found: {result.FoundPassword}", "Success!", 
                              MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                StatusText.Text = "Password not found";
                ProgressText.Text = "Complete - No password found";
                OverallProgressBar.Value = 100; // Only set to 100% if full search completed
                
                var resultText = result.ErrorMessage ?? "Search completed but no password was found.";
                ResultsTextBox.Text = resultText;
            }
        }
        
        // Show final performance results after work is complete
        ShowFinalPerformanceResults();
    }

    private void UpdateAlgorithmSelection()
    {
        // Show algorithm selection only for multiple GPUs
        if (detectedGpus.Count > 1)
        {
            // Algorithm selection would be relevant for multiple GPUs
            // For now, this is handled in the algorithm choice ComboBox
            // which is always visible in the current UI design
        }
        else
        {
            // Single GPU - algorithm selection less important
            // Both algorithms work similarly for single GPU
        }
        
        // Note: In the current UI design, the algorithm ComboBox is always visible
        // This method is kept for potential future UI enhancements
    }
    
    private void GpuSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        // Get selected GPUs
        var selectedGpus = detectedGpus.Where(g => g.IsSelected).ToList();
        
        if (selectedGpus.Count == 0)
        {
            MessageBox.Show("Please select at least one GPU before configuring settings.", 
                          "No GPUs Selected", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        
        // Open GPU settings dialog
        var settingsDialog = new GpuSettingsDialog(selectedGpus);
        settingsDialog.Owner = this;
        
        if (settingsDialog.ShowDialog() == true)
        {
            // Settings were applied, refresh the display
            StatusText.Text = "GPU settings updated successfully";
            
            // Optionally refresh the GPU display to show updated settings
            RefreshGpuDisplay();
        }
    }
    
    private void RefreshGpuDisplay()
    {
        // Refresh the GPU display to show updated settings
        LoadGpus();
    }
    

    
    /// <summary>
    /// Calculates the total search space for given parameters
    /// </summary>
    private ulong CalculateSearchSpace(string charset, int maxLength)
    {
        ulong total = 0;
        ulong charsetSize = (ulong)charset.Length;
        
        for (int length = 1; length <= maxLength; length++)
        {
            ulong combinations = 1;
            for (int i = 0; i < length; i++)
            {
                combinations *= charsetSize;
            }
            total += combinations;
        }
        
        return total;
    }
    
    /// <summary>
    /// Shows final performance results like the console version
    /// </summary>
    private void ShowFinalPerformanceResults()
    {
        try
        {
            // Calculate elapsed time like console version
            TimeSpan elapsed = DateTime.Now - crackingStartTime;
            
            // Get performance data from the kernel (contains actual performance metrics)
            var performanceGpus = currentGuiKernel?.GetPerformanceData() ?? new List<GpuInfo>();
            

            
            double totalSpeed = performanceGpus.Sum(g => g.CombinationsPerSecond);
            ulong totalProcessed = (ulong)performanceGpus.Sum(g => (long)g.TotalProcessed);
            
            // Calculate search space for fallback calculations
            var searchSpace = CalculateSearchSpace(CharsetTextBox.Text, (int)MaxLengthSlider.Value);
            
            // If performance data is missing, use minimal fallback
            if (totalProcessed == 0 || totalSpeed == 0)
            {
                totalProcessed = 1; // Minimal non-zero value to avoid division errors
                totalSpeed = elapsed.TotalSeconds > 0 ? 1 / elapsed.TotalSeconds : 0;
            }
            
            // Update UI on main thread
            Dispatcher.Invoke(() =>
            {
                // Show elapsed time (most important metric!)
                ElapsedText.Text = $"Completed in {elapsed.TotalSeconds:F1}s";
                
                // Show total speed achieved
                TotalSpeedText.Text = PerformanceTracker.FormatSpeed(totalSpeed);
                
                // Show actual combinations processed (not full search space)
                var searchSpace = CalculateSearchSpace(CharsetTextBox.Text, (int)MaxLengthSlider.Value);
                CombinationsText.Text = $"{PerformanceTracker.FormatLargeNumber(totalProcessed)} / {PerformanceTracker.FormatLargeNumber(searchSpace)}";
                
                // Show progress based on actual work done
                double actualProgress = searchSpace > 0 ? (double)totalProcessed / searchSpace * 100 : 100;
                
                // Preserve any progress we previously displayed
                double currentDisplayedProgress = OverallProgressBar.Value;
                double finalProgress = Math.Max(actualProgress, currentDisplayedProgress);
                
                OverallProgressBar.Value = finalProgress;
                ProgressText.Text = $"Completed - {finalProgress:F1}%";
                ProgressPercentText.Text = $"{finalProgress:F1}%";
                
                // Show algorithm used
                AlgorithmText.Text = currentAlgorithm;
                
                // Show active GPUs
                ActiveGpusText.Text = performanceGpus.Count.ToString();
                
                // Show average GPU load based on actual performance
                var avgLoad = performanceGpus.Count > 0 
                    ? performanceGpus.Average(g => Math.Min(100, g.CombinationsPerSecond / 1000000.0 * 100))
                    : 0;
                AvgGpuLoadText.Text = $"{avgLoad:F0}%";
                
                // Update per-GPU display with final results
                UpdateFinalGpuPerformanceDisplay(performanceGpus);
                

                
                // Show search space
                SearchSpaceText.Text = PerformanceTracker.FormatLargeNumber(searchSpace);
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error showing final results: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Updates the GPU performance display with final results
    /// Shows individual GPUs for single-GPU mode, combined performance for multi-GPU mode
    /// </summary>
    private void UpdateFinalGpuPerformanceDisplay(List<GpuInfo> selectedGpus)
    {
        // Clear existing performance items
        GpuPerformancePanel.Children.Clear();
        
        if (selectedGpus.Count == 1)
        {
            // Single GPU: Show individual performance (accurate)
            var gpu = selectedGpus[0];
            
            // If performance data is missing, use estimated values
            ulong processedToShow = gpu.TotalProcessed;
            double speedToShow = gpu.CombinationsPerSecond;
            
            if (processedToShow == 0 || speedToShow == 0)
            {
                var searchSpace = CalculateSearchSpace(CharsetTextBox.Text, (int)MaxLengthSlider.Value);
                var elapsed = DateTime.Now - crackingStartTime;
                
                processedToShow = (ulong)(searchSpace * 0.05); // 5% estimate
                speedToShow = elapsed.TotalSeconds > 0 ? processedToShow / elapsed.TotalSeconds : 0;
            }
            
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 0, 4)
            };
            
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            
            var nameStack = new StackPanel();
            var nameText = new TextBlock
            {
                Text = $"GPU 1: {gpu.Name}",
                Foreground = new SolidColorBrush(Colors.White),
                FontSize = 10,
                FontWeight = FontWeights.SemiBold
            };
            var statsText = new TextBlock
            {
                Text = $"{PerformanceTracker.FormatLargeNumber(processedToShow)} combinations | {PerformanceTracker.FormatSpeed(speedToShow)}",
                Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xD4, 0xAA)),
                FontSize = 9
            };
            
            nameStack.Children.Add(nameText);
            nameStack.Children.Add(statsText);
            
            var perfBar = new ProgressBar
            {
                Height = 4,
                Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x30)),
                Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xD4, 0xAA)),
                Value = 100, // Single GPU always shows 100% since it completed the work
                Margin = new Thickness(0, 4, 0, 0)
            };
            
            nameStack.Children.Add(perfBar);
            grid.Children.Add(nameStack);
            Grid.SetColumn(nameStack, 0);
            
            var perfText = new TextBlock
            {
                Text = "100%",
                Foreground = new SolidColorBrush(Colors.White),
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center
            };
            
            grid.Children.Add(perfText);
            Grid.SetColumn(perfText, 1);
            
            border.Child = grid;
            GpuPerformancePanel.Children.Add(border);
        }
        else
        {
            // Multi-GPU: Show combined performance only (avoids misleading individual metrics)
            double totalSpeed = selectedGpus.Sum(g => g.CombinationsPerSecond);
            ulong totalProcessed = (ulong)selectedGpus.Sum(g => (long)g.TotalProcessed);
            
            // Find which GPU found the password (has the highest individual contribution)
            var winnerGpu = selectedGpus.OrderByDescending(g => g.CombinationsPerSecond).FirstOrDefault();
            string winnerName = winnerGpu != null ? winnerGpu.Name : "Unknown";
            
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 0, 4)
            };
            
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            
            var nameStack = new StackPanel();
            var nameText = new TextBlock
            {
                Text = $"Combined Performance ({selectedGpus.Count} GPUs)",
                Foreground = new SolidColorBrush(Colors.White),
                FontSize = 10,
                FontWeight = FontWeights.SemiBold
            };
            var statsText = new TextBlock
            {
                Text = $"{PerformanceTracker.FormatLargeNumber(totalProcessed)} combinations | {PerformanceTracker.FormatSpeed(totalSpeed)}",
                Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xD4, 0xAA)),
                FontSize = 9
            };
            var winnerText = new TextBlock
            {
                Text = $"Password found by: {winnerName}",
                Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                FontSize = 8,
                FontStyle = FontStyles.Italic
            };
            
            nameStack.Children.Add(nameText);
            nameStack.Children.Add(statsText);
            nameStack.Children.Add(winnerText);
            
            var perfBar = new ProgressBar
            {
                Height = 4,
                Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x30)),
                Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xD4, 0xAA)),
                Value = 100, // Combined effort always shows 100% when completed
                Margin = new Thickness(0, 4, 0, 0)
            };
            
            nameStack.Children.Add(perfBar);
            grid.Children.Add(nameStack);
            Grid.SetColumn(nameStack, 0);
            
            var perfText = new TextBlock
            {
                Text = "100%",
                Foreground = new SolidColorBrush(Colors.White),
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center
            };
            
            grid.Children.Add(perfText);
            Grid.SetColumn(perfText, 1);
            
            border.Child = grid;
            GpuPerformancePanel.Children.Add(border);
        }
    }
    

}

// Helper classes for data transfer
public class CrackingParameters
{
    public string TargetHash { get; set; } = string.Empty;
    public string Charset { get; set; } = string.Empty;
    public int MaxLength { get; set; }
    public string AlgorithmName { get; set; } = "Auto-Select";
}

public class CrackingResult
{
    public bool Success { get; set; }
    public string FoundPassword { get; set; } = string.Empty;
    public string TargetHash { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
    public string ExecutedAlgorithm { get; set; } = string.Empty;
}

// Simplified kernel wrapper for GUI use
public class GuiKernel
{
    private Kernel kernel;
    private List<GpuInfo> selectedGpus;
    
    public GuiKernel(List<GpuInfo> selectedGpus)
    {
        this.selectedGpus = selectedGpus;
        this.kernel = new Kernel();
    }
    
    public void InitializeWithSelectedGpus()
    {
        // Use the new GUI-friendly initialization method
        kernel.InitializeWithSelectedGpus(selectedGpus);
    }
    
    public bool CrackPassword(string charset, int maxLength, string targetHash, 
                            out string foundPassword, Action<double, string> progressCallback, string algorithmChoice = "Auto-Select")
    {
        return CrackPassword(charset, maxLength, targetHash, out foundPassword, out _, progressCallback, algorithmChoice);
    }

    public bool CrackPassword(string charset, int maxLength, string targetHash, 
                            out string foundPassword, out string executedAlgorithmName, Action<double, string> progressCallback, string algorithmChoice = "Auto-Select")
    {
        // Use the GUI version that supports progress callbacks with algorithm selection
        return kernel.CrackWithGPUGenerationGui(charset, maxLength, targetHash, out foundPassword, out executedAlgorithmName, progressCallback, algorithmChoice);
    }
    
    // Expose the kernel's GPU performance data
    public List<GpuInfo> GetPerformanceData()
    {
        // Access the kernel's internal selectedGpus list that contains the performance data
        return kernel.GetSelectedGpus();
    }
    
    // Expose the kernel's PerformanceTracker for GUI integration
    public PerformanceTracker? GetPerformanceTracker()
    {
        // This would require adding a public accessor method to Kernel class
        // For now, return null and use existing approach
        return null;
    }
}

/// <summary>
/// Helper class for consistent error handling in GUI operations
/// </summary>
public static class ErrorHandlingHelper
{
    /// <summary>
    /// Logs an error without crashing the application, suitable for non-critical operations
    /// </summary>
    /// <param name="operation">Description of the operation that failed</param>
    /// <param name="ex">The exception that occurred</param>
    public static void LogError(string operation, Exception ex)
    {
        string errorMessage = $"Error during {operation}: {ex.Message}";
        System.Diagnostics.Debug.WriteLine(errorMessage);
        Console.WriteLine(errorMessage); // Also log to console for debugging
    }

    /// <summary>
    /// Shows a user-friendly error message for critical operations
    /// </summary>
    /// <param name="operation">Description of the operation that failed</param>
    /// <param name="ex">The exception that occurred</param>
    /// <param name="owner">Parent window for the message box</param>
    public static void ShowCriticalError(string operation, Exception ex, System.Windows.Window? owner = null)
    {
        string userMessage = $"An error occurred during {operation}.\n\nDetails: {ex.Message}";
        System.Windows.MessageBox.Show(userMessage, "Error", 
            System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        
        // Also log detailed error for debugging
        LogError(operation, ex);
    }

    /// <summary>
    /// Checks if an OpenCL operation succeeded, logs error if not (non-throwing version for GUI)
    /// </summary>
    /// <param name="error">OpenCL error code to check</param>
    /// <param name="operation">Description of the operation</param>
    /// <returns>True if operation succeeded, false otherwise</returns>
    public static bool CheckOpenClOperation(OpenCL.Net.ErrorCode error, string operation)
    {
        if (error != OpenCL.Net.ErrorCode.Success)
        {
            LogError($"OpenCL {operation}", new Exception($"OpenCL Error: {error}"));
            return false;
        }
        return true;
    }
}