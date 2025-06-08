using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Media;
using System.Text;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using Hardcodet.Wpf.TaskbarNotification;
using System.Windows.Navigation;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace MultiMonitor
{
    public partial class MainWindow : Window
    {
        private ObservableCollection<ScriptInfo> _availableScripts = new();
        private ObservableCollection<ScriptTag> _currentTags = new();
        private ObservableCollection<TemplateSentence> _currentTemplates = new();
        private string _scriptsDirectory;
        private ScriptInfo? _currentScript;
        private Process? _runningProcess;
        private HwndSource? _trayHwndSource;
        private ContextMenu _trayMenu;

        // Add a field for the tray icon
        private TaskbarIcon? _trayIcon;

        private string _monitorUrl = string.Empty;
        private string _foundUrl = string.Empty;

        public string MonitorUrl
        {
            get => _monitorUrl;
            set
            {
                _monitorUrl = value;
                Debug.WriteLine($"MonitorUrl updated to: {_monitorUrl}");
                OnPropertyChanged();
            }
        }

        public string FoundUrl
        {
            get => _foundUrl;
            set
            {
                _foundUrl = value;
                Debug.WriteLine($"FoundUrl updated to: {_foundUrl}");
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public MainWindow()
        {
            InitializeComponent();

            Title = "MultiMonitor";
            DataContext = this; // Set the DataContext for binding

            MonitorUrl = "";
            FoundUrl = "";

            // Initialize App Data Folders
            _scriptsDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MultiMonitor", "Scripts");

            if (!Directory.Exists(_scriptsDirectory))
            {
                Directory.CreateDirectory(_scriptsDirectory);
            }

            // Initialize UI
            LoadAvailableScripts();
            TagsRepeater.ItemsSource = _currentTags;
            TemplatesRepeater.ItemsSource = _currentTemplates;

            // Initialize tray icon and menu
            InitializeTrayIcon();
        }

        // Initialize tray icon and menu
        private void InitializeTrayIcon()
        {
            // Create the tray icon
            _trayIcon = new TaskbarIcon
            {
                Icon = new System.Drawing.Icon("icon.ico"), // Ensure the file is in the Resources folder
                ToolTipText = "MultiMonitor",
                ContextMenu = new System.Windows.Controls.ContextMenu()
            };

            // Add menu items to the tray menu
            var stopMenuItem = new MenuItem { Header = "Stop" };
            stopMenuItem.Click += (s, e) => StopScriptFromTray();
            var exitMenuItem = new MenuItem { Header = "Exit" };
            exitMenuItem.Click += (s, e) => ExitApplication();
            _trayIcon.ContextMenu.Items.Add(stopMenuItem);
            _trayIcon.ContextMenu.Items.Add(exitMenuItem);

            // Handle double-click to restore the window
            _trayIcon.TrayMouseDoubleClick += (s, e) => RestoreWindow();
        }

        // Update tray icon based on script state
        private void UpdateTrayIcon()
        {
            var iconUri = _runningProcess != null && !_runningProcess.HasExited
                ? new Uri("pack://application:,,,/running_icon.ico")
                : new Uri("pack://application:,,,/icon.ico");

            // Load the icon as a BitmapFrame (if needed for display elsewhere)
            var icon = BitmapFrame.Create(iconUri);            
        }

        private async void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentScript != null)
            {
                MonitorUrlTextBox.Text = "";
                FoundUrlTextBox.Text = "";
                await RunScriptWithPreviewAsync(); // Only reset the UI and run the preview
                UpdateStatus("Reset to preview state");
            }

        }

        private async Task RunScriptWithPreviewAsync()
        {
            if (_currentScript == null)
            {
                Debug.WriteLine("No script selected to run.");
                UpdateStatus("No script selected to run.");
                return;
            }

            UpdateStatus($"Running {_currentScript.DisplayName} with preview...");
            Debug.WriteLine($"Starting script: {_currentScript.FilePath}");

            try
            {
                // Clear existing data
                _currentTags.Clear();
                _currentTemplates.Clear();                // Run the script with --preview argument
                // Find the project root directory by looking for the embedded Python
                var currentDir = AppDomain.CurrentDomain.BaseDirectory;
                string pythonExe = null;
                
                // Try to find python.exe in various locations
                var possiblePaths = new[]
                {
                    // For development environment - look in source directory
                    Path.Combine(Directory.GetParent(currentDir)?.Parent?.Parent?.Parent?.FullName ?? "", "python", "python.exe"),
                    // For published app - look relative to executable
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "python", "python.exe"),
                    // For development debugging - look in project output
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "python", "python.exe")
                };
                
                foreach (var path in possiblePaths)
                {
                    if (File.Exists(path))
                    {
                        pythonExe = path;
                        break;
                    }
                }
                  if (pythonExe == null)
                {
                    throw new FileNotFoundException("Python executable not found. Please ensure python.exe is available in the python folder.");
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = pythonExe,
                    Arguments = $"\"{_currentScript.FilePath}\" --preview",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    EnvironmentVariables = 
                    {
                        ["PYTHONPATH"] = Path.GetDirectoryName(pythonExe),
                        ["PYTHONHOME"] = Path.GetDirectoryName(pythonExe)
                    }
                };

                using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

                var outputBuilder = new StringBuilder();
                var errorBuilder = new StringBuilder();

                process.OutputDataReceived += (s, args) =>
                {
                    if (!string.IsNullOrEmpty(args.Data))
                    {
                        outputBuilder.AppendLine(args.Data);
                        Debug.WriteLine($"[STDOUT] {args.Data}"); // Log to Visual Studio console
                    }
                };

                process.ErrorDataReceived += (s, args) =>
                {
                    if (!string.IsNullOrEmpty(args.Data))
                    {
                        errorBuilder.AppendLine(args.Data);
                        Debug.WriteLine($"[STDERR] {args.Data}"); // Log errors to Visual Studio console
                    }
                };

                Debug.WriteLine("Starting the Python process...");
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                Debug.WriteLine("Python process started.");

                await process.WaitForExitAsync();

                if (process.ExitCode == 0)
                {
                    var output = outputBuilder.ToString();
                    if (IsJson(output))
                    {
                        ProcessPreviewOutput(output);
                        UpdateStatus($"Preview loaded for {_currentScript.DisplayName}");
                    }
                    else
                    {
                        UpdateStatus(output.Trim());
                    }
                }
                else
                {
                    UpdateStatus($"Error running script: Exit code {process.ExitCode}");
                    Debug.WriteLine($"Script exited with code {process.ExitCode}");
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error: {ex.Message}");
                Debug.WriteLine($"Exception: {ex}");
            }
        }
        
        private Dictionary<string, TextBox> _argumentFields = new();
        private void AddArgumentField(string argName, string placeholder = "")
        {
            if (_argumentFields.ContainsKey(argName))
                return;

            // Ensure ArgumentsPanel has two rows
            if (ArgumentsPanel.RowDefinitions.Count < 2)
            {
                ArgumentsPanel.RowDefinitions.Clear();
                ArgumentsPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Labels
                ArgumentsPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // TextBoxes
            }

            // Add a new column for this argument
            int colIndex = ArgumentsPanel.ColumnDefinitions.Count;
            ArgumentsPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Create label
            var label = new Label
            {
                Content = argName,
                Padding = new Thickness(0, 0, 0, 0),
                Margin = new Thickness(0, 0, 0, 0),
                FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Left,
                ToolTip = placeholder
            };
            Grid.SetRow(label, 0);
            Grid.SetColumn(label, colIndex);

            // Create textbox
            var textBox = new TextBox
            {
                Tag = argName,
                Width = 120,
                Foreground = Brushes.Gray,
                Padding = new Thickness(2),
            };
            textBox.GotFocus += (s, e) =>
            {
                if (textBox.Text == placeholder)
                {
                    textBox.Text = "";
                    textBox.Foreground = Brushes.Black;
                }
            };
            textBox.LostFocus += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(textBox.Text))
                {
                    textBox.Text = placeholder;
                    textBox.Foreground = Brushes.Gray;
                }
            };
            Grid.SetRow(textBox, 1);
            Grid.SetColumn(textBox, colIndex);

            ArgumentsPanel.Children.Add(label);
            ArgumentsPanel.Children.Add(textBox);
            _argumentFields[argName] = textBox;
        }

        private void ProcessPreviewOutput(string output)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(output);
                var root = doc.RootElement;

                // Get the display name
                if (root.TryGetProperty("display_name", out var displayNameProp))
                {
                    if (_currentScript != null)
                    {
                        _currentScript.DisplayName = displayNameProp.GetString() ?? _currentScript.DisplayName;
                    }
                }

                // Refresh the dropdown menu to reflect the updated display_name
                RefreshScriptsComboBox();

                if (root.TryGetProperty("script_type", out var scriptTypeProp))
                {
                    if (_currentScript != null)
                    {
                        if (scriptTypeProp.ValueKind == JsonValueKind.String)
                        {
                            if (scriptTypeProp.GetString() == "static")
                            {

                            }
                            else if (scriptTypeProp.GetString() == "stream")
                            {
                                _currentScript.DisplayName += " (Stream)";
                            }

                            else if (scriptTypeProp.ValueKind == JsonValueKind.Number)
                            {
                                _currentScript.DisplayName += $" (Type {scriptTypeProp.GetInt32()})";
                            }
                        }
                    }

                    // Process tags
                    if (root.TryGetProperty("tags", out var tagsProp) && tagsProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var tagElement in tagsProp.EnumerateArray())
                        {
                            var tag = new ScriptTag
                            {
                                Name = tagElement.TryGetProperty("name", out var nameProp) ?
                                    nameProp.GetString() ?? string.Empty : string.Empty,
                                Detail = tagElement.TryGetProperty("detail", out var detailProp) ?
                                    detailProp.GetString() ?? string.Empty : string.Empty,
                                Tip = tagElement.TryGetProperty("tip", out var tipProp) ?
                                    tipProp.GetString() ?? string.Empty : string.Empty
                            };

                            // If detail is empty, use the name as detail
                            if (string.IsNullOrEmpty(tag.Detail))
                            {
                                tag.Detail = tag.Name;
                            }

                            _currentTags.Add(tag);
                        }
                    }

                    // Process template sentences
                    if (root.TryGetProperty("template_sentences", out var sentencesProp) && sentencesProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var sentence in sentencesProp.EnumerateArray())
                        {
                            string templateText = sentence.GetString() ?? string.Empty;

                            var templateSentence = new TemplateSentence
                            {
                                OriginalTemplate = templateText,
                                FormattedText = templateText
                            };

                            _currentTemplates.Add(templateSentence);
                        }
                    }

                    if (root.TryGetProperty("monitor", out var monitorProp) && monitorProp.ValueKind == JsonValueKind.Array)
                    {
                        var monitorUrls = monitorProp.EnumerateArray().Select(x => x.GetString()).Where(x => !string.IsNullOrEmpty(x));
                        string monitorUrl = monitorUrls.FirstOrDefault() ?? "No Monitor URL Available";
                        MonitorUrlTextBox.Text = monitorUrl;
                        Debug.WriteLine($"MonitorUrl set to: {monitorUrl}");
                    }
                }

                // Clear previous argument fields
                ArgumentsPanel.Children.Clear();
                _argumentFields.Clear();

                // Add dynamic argument fields from "fields" in JSON
                if (root.TryGetProperty("fields", out var fieldsProp) && fieldsProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var field in fieldsProp.EnumerateArray())
                    {
                        string name = field.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "" : "";
                        string tip = field.TryGetProperty("tip", out var tipProp) ? tipProp.GetString() ?? "" : "";

                        if (!string.IsNullOrEmpty(name))
                        {
                            AddArgumentField(name, tip);
                        }
                    }
                }
            }
            catch (JsonException ex)
            {
                UpdateStatus($"Error parsing JSON: {ex.Message}");
            }
        }        

        private void RefreshScriptsComboBox()
        {
            // Remember the current selection index
            int selectedIndex = ScriptsComboBox.SelectedIndex;

            // Update the items without triggering selection events
            ScriptsComboBox.SelectionChanged -= ScriptsComboBox_SelectionChanged;

            ScriptsComboBox.Items.Clear();
            foreach (var script in _availableScripts)
            {
                ScriptsComboBox.Items.Add(script.DisplayName);
            }

            // Restore the selection if it was valid
            if (selectedIndex >= 0 && selectedIndex < ScriptsComboBox.Items.Count)
            {
                ScriptsComboBox.SelectedIndex = selectedIndex;
            }

            // Reattach the event handler
            ScriptsComboBox.SelectionChanged += ScriptsComboBox_SelectionChanged;
        }

        private Dictionary<string, string> LoadDisplayNames()
        {
            string filePath = Path.Combine(_scriptsDirectory, "display_names.json");
            if (File.Exists(filePath))
            {
                string json = File.ReadAllText(filePath);
                return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
            }
            return new Dictionary<string, string>();
        }

        private void SaveDisplayNames(Dictionary<string, string> displayNames)
        {
            string filePath = Path.Combine(_scriptsDirectory, "display_names.json");
            string json = JsonSerializer.Serialize(displayNames, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filePath, json);
        }

        private bool IsJson(string input)
        {
            try
            {
                JsonDocument.Parse(input);
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }



        private void LoadAvailableScripts()
        {
            _availableScripts.Clear();
            ScriptsComboBox.Items.Clear();

            var displayNames = LoadDisplayNames(); // Add this line to define 'displayNames'

            if (Directory.Exists(_scriptsDirectory))
            {
                var scriptFiles = Directory.GetFiles(_scriptsDirectory, "*.py");
                foreach (var scriptFile in scriptFiles)
                {
                    var scriptInfo = new ScriptInfo
                    {
                        FileName = Path.GetFileName(scriptFile),
                        FilePath = scriptFile,
                        DisplayName = displayNames.ContainsKey(scriptFile) ? displayNames[scriptFile] : Path.GetFileNameWithoutExtension(scriptFile)
                    };

                    _availableScripts.Add(scriptInfo);
                    ScriptsComboBox.Items.Add(scriptInfo.DisplayName);
                }
            }

            UpdateStatus($"Loaded {_availableScripts.Count} scripts");
        }

        private void ImportButton_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Python Files (*.py)|*.py",
                Multiselect = true
            };

            if (openFileDialog.ShowDialog() == true)
            {
                foreach (var file in openFileDialog.FileNames)
                {
                    string destinationPath = Path.Combine(_scriptsDirectory, Path.GetFileName(file));
                    File.Copy(file, destinationPath, overwrite: true);
                }

                LoadAvailableScripts();
                UpdateStatus($"Imported {openFileDialog.FileNames.Length} scripts");
            }
        }

        private void CopyTemplateButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is TemplateSentence template)
            {
                Clipboard.SetText(template.FormattedText);
                UpdateStatus("Copied to clipboard");
            }
        }

        private void ResetTemplateButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is TemplateSentence template)
            {
                template.FormattedText = template.OriginalTemplate;
                UpdateStatus("Reset to template");
            }
        }

        private void EditTemplateButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is TemplateSentence template)
            {
                var dialog = new Window
                {
                    Title = "Edit Template",
                    Width = 400,
                    Height = 300,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = this
                };

                // Create the editing UI
                var editPanel = new Grid();
                editPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                editPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

                // TextBox for editing
                var textBox = new TextBox
                {
                    Text = template.FormattedText,
                    AcceptsReturn = true,
                    TextWrapping = TextWrapping.Wrap,
                    MinHeight = 100
                };
                Grid.SetRow(textBox, 0);
                editPanel.Children.Add(textBox);

                // Tags section header
                var headerTextBlock = new TextBlock
                {
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 10, 0, 5)
                };
                Grid.SetRow(headerTextBlock, 1);
                editPanel.Children.Add(headerTextBlock);

                // Tags panel with ScrollViewer for better UX with many tags
                var scrollViewer = new ScrollViewer
                {
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Margin = new Thickness(0, 35, 0, 0),
                    Height = 60
                };

                var tagsPanel = new WrapPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Left
                };

                scrollViewer.Content = tagsPanel;
                Grid.SetRow(scrollViewer, 1);
                editPanel.Children.Add(scrollViewer);

                foreach (var tag in _currentTags)
                {
                    var tagButton = new Button
                    {
                        Content = tag.Name,
                        Tag = tag,
                        Margin = new Thickness(3),
                        Padding = new Thickness(8, 4, 8, 4),
                        Background = new SolidColorBrush(Colors.LightGray)
                    };

                    tagButton.Click += (s, args) =>
                    {
                        if (tagButton.Content.ToString() == tag.Name)
                        {
                            tagButton.Content = tag.Detail;
                            tagButton.Background = new SolidColorBrush(Colors.LightBlue);
                        }
                        else
                        {
                            tagButton.Content = tag.Name;
                            tagButton.Background = new SolidColorBrush(Colors.DarkGray);
                        }
                    };

                    tagsPanel.Children.Add(tagButton);
                }

                dialog.Content = editPanel;

                var saveButton = new Button
                {
                    Content = "Save",
                    Width = 75,
                    Margin = new Thickness(5)
                };
                saveButton.Click += (s, args) =>
                {
                    template.FormattedText = textBox.Text;
                    UpdateStatus("Template updated");
                    dialog.Close();
                };

                var cancelButton = new Button
                {
                    Content = "Cancel",
                    Width = 75,
                    Margin = new Thickness(5)
                };
                cancelButton.Click += (s, args) => dialog.Close();

                var buttonPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right
                };
                buttonPanel.Children.Add(saveButton);
                buttonPanel.Children.Add(cancelButton);

                var mainPanel = new DockPanel();
                DockPanel.SetDock(buttonPanel, Dock.Bottom);
                mainPanel.Children.Add(buttonPanel);
                mainPanel.Children.Add(editPanel);

                dialog.Content = mainPanel;
                dialog.ShowDialog();
            }
        }

        private void TagButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is ScriptTag tag)
            {
                if (button.Content.ToString() == tag.Name)
                {
                    button.Content = tag.Detail;
                    button.Width = double.NaN;
                }
                else
                {
                    button.Content = tag.Name;
                    button.Width = double.NaN;
                }
            }
        }

        private async void ScriptsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ScriptsComboBox.SelectedIndex >= 0)
            {
                _currentScript = _availableScripts[ScriptsComboBox.SelectedIndex];
                Title = $"MultiMonitor - {_currentScript.DisplayName}";
                ArgumentsPanel.Children.Clear();
                _argumentFields.Clear();
                MonitorUrlTextBox.Text = "";
                FoundUrlTextBox.Text = "";
                // Automatically trigger --preview run
                await RunScriptWithPreviewAsync();
                
            }
        }

        private void RunButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentScript != null)
            {
                foreach (var kvp in _argumentFields)
                {
                    string argName = kvp.Key;
                    string argValue = kvp.Value.Text;
                    if (!string.IsNullOrWhiteSpace(argValue) && argValue != kvp.Value.Tag?.ToString())
                    {
                        Debug.WriteLine($"Argument: --{argName} \"{argValue}\"");
                    }
                    kvp.Value.IsReadOnly = true;
                }
                ScriptsComboBox.IsEnabled = false;
                ResetButton.IsEnabled = false;
                ResetButton.Background = Brushes.LightGray;
                _ = RunScriptAsync();
            }
            else
            {
                UpdateStatus("Please select a script first");
            }
        }

        private void UpdateStatus(string message)
        {
            StatusTextBlock.Text = $"[{DateTime.Now:HH:mm:ss}] {message}";
            Debug.WriteLine($"Status updated: {message}");
        }

        private void MonitorAddressPanel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
                OpenUrlIfValid(MonitorUrlTextBox.Text);
        }

        private void FoundAddressPanel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
                OpenUrlIfValid(FoundUrlTextBox.Text);
        }

        private void OpenUrlIfValid(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return;

            if (Uri.TryCreate(url, UriKind.Absolute, out var uriResult) &&
                (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps))
            {
                try
                {
                    Process.Start(new ProcessStartInfo(uriResult.AbsoluteUri) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to open URL: {ex.Message}");
                }
            }
        }

        private async Task RunScriptAsync()
        {
            if (_currentScript == null) return;

            // If a process is already running, stop it
            if (_runningProcess != null)
            {
                try
                {
                    if (!_runningProcess.HasExited)
                    {
                        _runningProcess.Kill();
                        await _runningProcess.WaitForExitAsync();
                    }
                }
                catch (Exception ex)
                {
                    UpdateStatus($"Error stopping process: {ex.Message}");
                }
                finally
                {
                    CleanupRunningProcess();
                }

                RunButton.Content = "Run";
                Title = "MultiMonitor"; // Reset the window title
                if (_trayIcon != null) _trayIcon.ToolTipText = "MultiMonitor"; // Reset the tray tooltip
                UpdateStatus("Script stopped");
                ScriptsComboBox.IsEnabled = true;
                ResetButton.IsEnabled = true;
                ResetButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E57373"));
                return;
            }

            UpdateStatus($"Running {_currentScript.DisplayName}...");
            RunButton.Content = "Stop";

            // Update the window title and tray tooltip to indicate the script is running
            Title = $"MultiMonitor - {_currentScript.DisplayName} - Running";
            if (_trayIcon != null) _trayIcon.ToolTipText = $"MultiMonitor - {_currentScript.DisplayName} - Running";

            try
            {
                // Find Python executable
                var currentDir = AppDomain.CurrentDomain.BaseDirectory;
                string pythonExe = null;

                var possiblePaths = new[]
                {
                    Path.Combine(Directory.GetParent(currentDir)?.Parent?.Parent?.Parent?.FullName ?? "", "python", "python.exe"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "python", "python.exe"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "python", "python.exe")
                };

                foreach (var path in possiblePaths)
                {
                    if (File.Exists(path))
                    {
                        pythonExe = path;
                        break;
                    }
                }

                if (pythonExe == null)
                {
                    UpdateStatus("Python executable not found. Please ensure python.exe is available in the python folder.");
                    RunButton.Content = "Run";
                    Title = "MultiMonitor"; // Reset the window title
                    if (_trayIcon != null) _trayIcon.ToolTipText = "MultiMonitor"; // Reset the tray tooltip
                    ScriptsComboBox.IsEnabled = true;
                    return;
                }

                var argList = new List<string>();
                foreach (var kvp in _argumentFields)
                {
                    var value = kvp.Value.Text;
                    if (!string.IsNullOrWhiteSpace(value) && value != kvp.Value.Tag?.ToString())
                    {
                        argList.Add($"--{kvp.Key} \"{value}\"");
                    }
                }
                var arguments = $"-u \"{_currentScript.FilePath}\" {string.Join(" ", argList)}";

                var startInfo = new ProcessStartInfo
                {
                    FileName = pythonExe,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    // Add timeout environment variable
                    EnvironmentVariables =
                    {
                        ["PYTHONPATH"] = Path.GetDirectoryName(pythonExe) ?? "",
                        ["PYTHONHOME"] = Path.GetDirectoryName(pythonExe) ?? "",
                        ["PYTHONUNBUFFERED"] = "1" // Ensure immediate output
                    }
                };

                _runningProcess = new Process
                {
                    StartInfo = startInfo,
                    EnableRaisingEvents = true
                };

                // Handle real-time output
                _runningProcess.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        Dispatcher.Invoke(() =>
                        {
                            Debug.WriteLine($"Python Output: {e.Data}");

                            if (IsJson(e.Data))
                            {
                                ProcessScriptOutput(e.Data);
                            }
                            else
                            {
                                UpdateStatus(e.Data);
                            }
                        });
                    }
                };

                // Handle real-time error output
                _runningProcess.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        Dispatcher.Invoke(() =>
                        {
                            Debug.WriteLine($"Python Error: {e.Data}");
                            UpdateStatus($"Error: {e.Data}");
                        });
                    }
                };

                // Set up a timeout
                var timeoutTask = Task.Delay(TimeSpan.FromMinutes(5)); // 5 minute timeout

                _runningProcess.Start();
                _runningProcess.BeginOutputReadLine();
                _runningProcess.BeginErrorReadLine();

                // Wait for either the process to exit or timeout
                var processTask = Task.Run(() => _runningProcess.WaitForExit());
                var completedTask = await Task.WhenAny(processTask, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    // Timeout occurred
                    UpdateStatus("Script timeout - stopping process");
                    try
                    {
                        _runningProcess.Kill();
                        await _runningProcess.WaitForExitAsync();
                    }
                    catch { }
                }

                Dispatcher.Invoke(() =>
                {
                    RunButton.Content = "Run";
                    Title = "MultiMonitor"; // Reset the window title
                    ResetButton.IsEnabled = true;
                    ResetButton.Background = new SolidColorBrush(Color.FromRgb(229, 115, 115)); // #E57373
                    foreach (var tb in _argumentFields.Values)
                        tb.IsReadOnly = false;
                    RunButton.Content = "Run";
                    if (_trayIcon != null) _trayIcon.ToolTipText = "MultiMonitor"; // Reset the tray tooltip
                    if (_runningProcess?.ExitCode == 0)
                    {
                        UpdateStatus("Script completed successfully");
                    }
                    else
                    {
                        UpdateStatus($"Script terminated: {_runningProcess?.ExitCode}");
                    }
                    CleanupRunningProcess();
                });
            }
            catch (Exception ex)
            {
                RunButton.Content = "Run";
                Title = "MultiMonitor"; // Reset the window title
                if (_trayIcon != null) _trayIcon.ToolTipText = "MultiMonitor"; // Reset the tray tooltip
                UpdateStatus($"Error: {ex.Message}");
                CleanupRunningProcess();
            }
        }

        private void CleanupRunningProcess()
        {
            if (_runningProcess != null)
            {
                _runningProcess.OutputDataReceived -= null;
                _runningProcess.ErrorDataReceived -= null;
                _runningProcess.Dispose();
                _runningProcess = null;
            }
        }        // Commands removed - using event handlers instead

        private void ProcessScriptOutput(string output)
        {
            try
            {
                Debug.WriteLine($"Raw JSON Input: {output}"); // Log the raw JSON input

                // Split the output into individual JSON objects
                var jsonObjects = output.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var jsonObject in jsonObjects)
                {
                    if (IsJson(jsonObject))
                    {
                        using JsonDocument doc = JsonDocument.Parse(jsonObject);
                        var root = doc.RootElement;

                        // Process output_sentences
                        if (root.TryGetProperty("output_sentences", out var outputSentencesProp) && outputSentencesProp.ValueKind == JsonValueKind.Array)
                        {
                            _currentTemplates.Clear(); // Clear existing templates
                            foreach (var sentence in outputSentencesProp.EnumerateArray())
                            {
                                string outputText = sentence.GetString() ?? string.Empty;

                                var outputSentence = new TemplateSentence
                                {
                                    OriginalTemplate = outputText,
                                    FormattedText = outputText
                                };

                                _currentTemplates.Add(outputSentence);
                            }

                            UpdateStatus("Updated.");
                        }

                        if (root.TryGetProperty("found", out var foundProp) && foundProp.ValueKind == JsonValueKind.String)
                        {
                            FoundUrl = foundProp.GetString() ?? "No Found URL Available";
                            FoundUrlTextBox.Text = FoundUrl;
                            Debug.WriteLine($"FoundUrl set to: {FoundUrl}");
                        }

                        // Process template_sentences (if applicable)
                        if (root.TryGetProperty("template_sentences", out var templateSentencesProp) && templateSentencesProp.ValueKind == JsonValueKind.Array)
                        {
                            _currentTemplates.Clear(); // Clear existing templates
                            foreach (var sentence in templateSentencesProp.EnumerateArray())
                            {
                                string templateText = sentence.GetString() ?? string.Empty;

                                var templateSentence = new TemplateSentence
                                {
                                    OriginalTemplate = templateText,
                                    FormattedText = templateText
                                };

                                _currentTemplates.Add(templateSentence);
                            }

                            UpdateStatus("Template sentences updated.");
                        }

                        // Process other properties (e.g., tags)
                        Dictionary<string, string> tagValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var property in root.EnumerateObject())
                        {
                            tagValues[property.Name] = property.Value.ValueKind switch
                            {
                                JsonValueKind.String => property.Value.GetString() ?? string.Empty,
                                JsonValueKind.Number => property.Value.GetRawText(),
                                JsonValueKind.True => "true",
                                JsonValueKind.False => "false",
                                JsonValueKind.Null => string.Empty,
                                _ => property.Value.ToString()
                            };
                        }

                        // Update template sentences with tag values
                        foreach (var template in _currentTemplates)
                        {
                            string text = template.FormattedText; // Use the current user-modified template

                            // Tokenize and replace placeholders
                            text = System.Text.RegularExpressions.Regex.Replace(text, "\\{(\\w+)\\}", match =>
                            {
                                string key = match.Groups[1].Value;
                                return tagValues.TryGetValue(key, out var value) ? value : match.Value; // Replace or keep the placeholder
                            });

                            // Update the formatted text
                            Dispatcher.Invoke(() =>
                            {
                                template.FormattedText = text;
                                Debug.WriteLine($"Updated FormattedText: {template.FormattedText}"); // Log the updated text

                            });
                        }
                        try
                        {
                            var soundFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bell.wav");
                            var soundPlayer = new System.Media.SoundPlayer(soundFilePath);
                            soundPlayer.Play();
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error playing sound: {ex.Message}");
                        }
                    }
                }
            }
            catch (JsonException ex)
            {
                UpdateStatus($"Error parsing JSON: {ex.Message}");
                Debug.WriteLine($"JSON Parsing Error: {ex}"); // Log the exception details
            }
            catch (InvalidOperationException ex)
            {
                UpdateStatus($"Error processing JSON: {ex.Message}");
                Debug.WriteLine($"JSON Processing Error: {ex}"); // Log the exception details
            }
        }

        private void AddTemplateSentenceButton_Click(object sender, RoutedEventArgs e)
        {
            var newTemplate = new TemplateSentence
            {
                OriginalTemplate = string.Empty,
                FormattedText = string.Empty
            };

            _currentTemplates.Add(newTemplate);
            UpdateStatus("Added a new template sentence.");
        }

        private void TagTextBlock_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is TextBlock textBlock && textBlock.DataContext is ScriptTag tag)
            {
                if (textBlock.Text == tag.Name)
                {
                    textBlock.Text = tag.Detail;
                    textBlock.Foreground = new SolidColorBrush(Colors.DarkGray);
                }
                else
                {
                    textBlock.Text = tag.Name;
                    textBlock.Foreground = new SolidColorBrush(Colors.Black);
                }
            }
        }

        private void TagTextBlock_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is TextBlock textBlock && textBlock.DataContext is ScriptTag tag)
            {
                textBlock.ToolTip = tag.Tip;
            }
        }

        // Override OnClosing to minimize to tray
        protected override void OnClosing(CancelEventArgs e)
        {
            if (_runningProcess != null && !_runningProcess.HasExited)
            {
                e.Cancel = true; // Cancel the close operation
                Hide(); // Minimize to tray
                _trayIcon?.ShowBalloonTip("MultiMonitor", "The app is minimized to the tray.", BalloonIcon.Info);
            }
            else
            {
                _trayIcon?.Dispose(); // Clean up tray icon
            }
        }

        // Stop script from tray
        private void StopScriptFromTray()
        {
            if (_runningProcess != null)
            {
                RunScriptAsync(); // Call the same method as the "Stop" button
            }
        }

        // Exit application
        private void ExitApplication()
        {
            _trayIcon?.Dispose();
            Application.Current.Shutdown();
        }

        // Restore window from tray
        private void RestoreWindow()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = e.Uri.AbsoluteUri,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error opening link: {ex.Message}");
            }
        }
    }

    public class ScriptInfo
    {
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
    }

    public class ScriptTag
    {
        public string Name { get; set; } = string.Empty;
        public string Detail { get; set; } = string.Empty;
        public string Tip { get; set; } = string.Empty;
    }

    public class TemplateSentence : INotifyPropertyChanged
    {
        public string OriginalTemplate { get; set; } = string.Empty;

        private string _formattedText = string.Empty;
        public string FormattedText
        {
            get => _formattedText;
            set
            {
                if (_formattedText != value)
                {
                    _formattedText = value;
                    OnPropertyChanged(nameof(FormattedText));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class RelayCommand<T> : ICommand
    {
        private readonly Action<T> _execute;
        private readonly Predicate<T>? _canExecute;

        public RelayCommand(Action<T> execute, Predicate<T>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter)
        {
            return _canExecute == null || (parameter is T t && _canExecute(t));
        }

        public void Execute(object? parameter)
        {
            if (parameter is T t)
            {
                _execute(t);
            }
        }

        public event EventHandler? CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }
    }
}