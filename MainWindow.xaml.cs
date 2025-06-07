using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

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

        public MainWindow()
        {
            InitializeComponent();

            Title = "MultiMonitor";
            DataContext = this;

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
        }

        private async void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentScript != null)
            {
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
                _currentTemplates.Clear();

                // Run the script with --preview argument
                var startInfo = new ProcessStartInfo
                {
                    FileName = "python", // Ensure Python is accessible
                    Arguments = $"\"{_currentScript.FilePath}\" --preview",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
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

                // Refresh the dropdown menu to reflect the updated display_name
                RefreshScriptsComboBox();
            }
            catch (JsonException ex)
            {
                UpdateStatus($"Error parsing JSON: {ex.Message}");
            }
        }

            private void RefreshScriptsComboBox()
        {
            ScriptsComboBox.Items.Clear();
            foreach (var script in _availableScripts)
            {
                ScriptsComboBox.Items.Add(script.DisplayName);
            }
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
                UpdateStatus("Template copied to clipboard");
            }
        }

        private void ResetTemplateButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is TemplateSentence template)
            {
                template.FormattedText = template.OriginalTemplate;
                UpdateStatus("Template reset to original");
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
                            tagButton.Background = new SolidColorBrush(Colors.LightGray);
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

                // Automatically trigger --preview run
                await RunScriptWithPreviewAsync();
            }
        }

        private void RunButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentScript != null)
            {
                _ = RunScriptAsync(); // Trigger the RunScriptAsync function
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

        private async Task RunScriptAsync()
        {
            if (_currentScript == null) return;

            // If a process is already running, stop it
            if (_runningProcess != null)
            {
                try
                {
                    if (_runningProcess == null || _runningProcess.HasExited)
                    {
                        UpdateStatus("No running process to stop.");
                        RunButton.Content = "Run";
                        return;
                    }

                    _runningProcess.Kill(); // Terminate the process
                    await _runningProcess.WaitForExitAsync(); // Ensure it exits completely
                }
                catch (InvalidOperationException)
                {
                    // Process might have already exited, ignore this exception
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
                UpdateStatus("Script stopped");
                return;
            }

            UpdateStatus($"Running {_currentScript.DisplayName}...");
            RunButton.Content = "Stop";

            try
            {
                // Prepare the templates with the edited sentences
                var formattedTemplates = _currentTemplates.Select(t => t.FormattedText).ToList();

                // Optionally, save the formatted templates to a temporary file or pass them as arguments
                string tempFilePath = Path.Combine(Path.GetTempPath(), "formatted_templates.json");
                File.WriteAllText(tempFilePath, JsonSerializer.Serialize(formattedTemplates));

                // Configure the process to read real-time output
                var startInfo = new ProcessStartInfo
                {
                    FileName = "python",
                    Arguments = $"-u \"{_currentScript.FilePath}\" \"{tempFilePath}\"", // Pass the formatted templates file as an argument
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
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
                            if (IsJson(e.Data))
                            {
                                ProcessScriptOutput(e.Data); // Process JSON for placeholders
                            }
                            else
                            {
                                UpdateStatus(e.Data); // Display plain text output
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
                            UpdateStatus($"Error: {e.Data}");
                        });
                    }
                };

                // Start the process and begin reading output
                _runningProcess.Start();
                _runningProcess.BeginOutputReadLine();
                _runningProcess.BeginErrorReadLine();

                // Wait for the process to exit
                await Task.Run(() => _runningProcess.WaitForExit());

                Dispatcher.Invoke(() =>
                {
                    RunButton.Content = "Run";
                    if (_runningProcess != null)
                    {
                    }
                    else
                    {
                        UpdateStatus("Script stopped");
                    }
                    CleanupRunningProcess();
                });
            }
            catch (Exception ex)
            {
                RunButton.Content = "Run";
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
                Debug.WriteLine($"Raw JSON Output: {output}"); // Log the raw JSON output

                using JsonDocument doc = JsonDocument.Parse(output);
                var root = doc.RootElement;

                // Get the tag values
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

                // Process and update the template sentences
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

                // Play the bell sound after updating the templates
                Dispatcher.Invoke(() =>
                {
                    var player = new System.Media.SoundPlayer("bell.wav");
                    player.Play();
                });
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