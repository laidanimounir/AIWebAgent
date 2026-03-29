using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Playwright;
using Newtonsoft.Json;
using System.Collections.Generic;

namespace AIWebAgent
{
    public partial class MainWindow : Window
    {
        private const string GROQ_API_KEY = "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";
        private const string GROQ_MODEL = "llama-3.3-70b-versatile";
        private bool _isCloudMode = true;
        private IPlaywright _playwright;
        private IBrowser _browser;
        private IPage _page;

        public MainWindow()
        {
            InitializeComponent();
            MainWebView.CoreWebView2InitializationCompleted += MainWebView_CoreWebView2InitializationCompleted;
            InitializeBrowserAsync();
            _ = InitializePlaywrightAsync();
        }

        private async void InitializeBrowserAsync()
        {
            try
            {
                SetStatus("🟡 جاري تشغيل المتصفح...");
                string userDataFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "AIWebAgentProfile");
                var options = new CoreWebView2EnvironmentOptions("--remote-debugging-port=9222");
                var environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder, options);
                await MainWebView.EnsureCoreWebView2Async(environment);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"خطأ: {ex.Message}");
            }
        }

        private async System.Threading.Tasks.Task InitializePlaywrightAsync()
        {
            try
            {
                _playwright = await Playwright.CreateAsync();
                _browser = await _playwright.Chromium.ConnectOverCDPAsync("http://localhost:9222");
                var context = _browser.Contexts.Count > 0 ? _browser.Contexts[0] : await _browser.NewContextAsync();
                _page = context.Pages.Count > 0 ? context.Pages[0] : await context.NewPageAsync();
            }
            catch (Exception ex)
            {
                SetStatus($"🔴 خطأ Playwright: {ex.Message}");
            }
        }

        private void MainWebView_CoreWebView2InitializationCompleted(object sender, CoreWebView2InitializationCompletedEventArgs e)
        {
            if (e.IsSuccess)
            {
                MainWebView.CoreWebView2.Navigate(UrlTextBox.Text);
                SetStatus("🟢 المتصفح جاهز");
            }
            else
            {
                SetStatus("🔴 فشل تشغيل المتصفح");
            }
        }

        private void AiModeToggle_Checked(object sender, RoutedEventArgs e)
        {
            _isCloudMode = true;
            AiModeToggle.Content = "☁️ سحابي (Groq)";
            AiModeToggle.Background = new SolidColorBrush(Color.FromRgb(40, 167, 69));
            SetStatus("🟢 وضع السحابة (Groq) مفعّل");
        }

        private void AiModeToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            _isCloudMode = false;
            AiModeToggle.Content = "💻 محلي (Ollama)";
            AiModeToggle.Background = new SolidColorBrush(Color.FromRgb(108, 117, 125));
            SetStatus("💻 وضع المحلي (Ollama) مفعّل");
        }

        private async void SendCommand_Click(object sender, RoutedEventArgs e)
        {
            string userMessage = CommandInput.Text;
            if (string.IsNullOrWhiteSpace(userMessage)) return;

            AddMessage(userMessage, isUser: true);
            CommandInput.Clear();

            try
            {
                SetStatus("🟡 جاري قراءة الصفحة...");
                if (_page == null) await InitializePlaywrightAsync();
                string pageTitle = await _page.TitleAsync();
                string pageContent = await _page.InnerTextAsync("body");
                if (pageContent.Length > 2000) pageContent = pageContent.Substring(0, 2000);

                SetStatus("🟡 الوكيل يفكر...");
                string response;
                if (_isCloudMode)
                    response = await AskGroqAsync(userMessage, pageTitle, pageContent);
                else
                    response = await AskOllamaAsync(userMessage, pageTitle, pageContent);

                AddMessage(response, isUser: false);
                SetStatus("🟢 جاهز");
            }
            catch (Exception ex)
            {
                AddMessage($"خطأ: {ex.Message}", isUser: false, isError: true);
                SetStatus("🔴 حدث خطأ");
            }
        }

        private async System.Threading.Tasks.Task<string> AskGroqAsync(string userMessage, string pageTitle, string pageContent)
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {GROQ_API_KEY}");

            string systemPrompt = @"أنت وكيل ذكي مدمج في متصفح ويب.
عندما يطلب المستخدم فتح موقع معين، رد بهذا التنسيق فقط:
ACTION:NAVIGATE:https://www.example.com
عندما يطلب المستخدم البحث عن شيء، رد بهذا التنسيق فقط:
ACTION:SEARCH:كلمة البحث
عندما يطلب المستخدم معلومات أو يريد محادثة عادية، رد بهذا التنسيق فقط:
ACTION:REPLY:ردك هنا باللغة العربية
قواعد مهمة:
- لا تضيف أي كلام قبل أو بعد سطر ACTION
- الرد يجب أن يبدأ دائماً بـ ACTION:
- إذا طلب المستخدم يوتيوب، استخدم https://www.youtube.com
- إذا طلب المستخدم جوجل، استخدم https://www.google.com
- الصفحة الحالية عنوانها: " + pageTitle;

            var requestBody = new
            {
                model = GROQ_MODEL,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userMessage }
                },
                max_tokens = 300
            };

            var json = JsonConvert.SerializeObject(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await client.PostAsync("https://api.groq.com/openai/v1/chat/completions", content);
            var responseJson = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode) return $"Groq Error: {responseJson}";

            var result = JsonConvert.DeserializeObject<Dictionary<string, object>>(responseJson);
            var choices = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(result["choices"].ToString());
            var message = JsonConvert.DeserializeObject<Dictionary<string, object>>(choices[0]["message"].ToString());
            string aiResponse = message["content"].ToString().Trim();

            return ProcessAction(aiResponse);
        }

        private async System.Threading.Tasks.Task<string> AskOllamaAsync(string userMessage, string pageTitle, string pageContent)
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(120);

            string systemPrompt = $@"أنت وكيل ذكي مدمج في متصفح ويب.
الصفحة الحالية: {pageTitle}
محتوى الصفحة: {pageContent.Substring(0, Math.Min(500, pageContent.Length))}
عندما يطلب المستخدم فتح موقع، رد بهذا التنسيق فقط:
ACTION:NAVIGATE:https://www.example.com
عندما يطلب البحث:
ACTION:SEARCH:كلمة البحث
عندما يريد محادثة عادية:
ACTION:REPLY:ردك هنا
لا تضيف أي كلام آخر غير سطر ACTION";

            var requestBody = new
            {
                model = "qwen3:4b",
                stream = false,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userMessage }
                }
            };

            var json = JsonConvert.SerializeObject(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await client.PostAsync("http://localhost:11434/api/chat", content);
            var responseJson = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode) return $"Ollama Error: {responseJson}";

            var result = JsonConvert.DeserializeObject<Dictionary<string, object>>(responseJson);
            var message = JsonConvert.DeserializeObject<Dictionary<string, object>>(result["message"].ToString());
            string aiResponse = message["content"].ToString().Trim();

            return ProcessAction(aiResponse);
        }

        private string ProcessAction(string aiResponse)
        {
            if (aiResponse.StartsWith("ACTION:NAVIGATE:"))
            {
                string url = aiResponse.Replace("ACTION:NAVIGATE:", "").Trim();
                Dispatcher.Invoke(() => {
                    UrlTextBox.Text = url;
                    MainWebView.CoreWebView2.Navigate(url);
                });
                return $"✅ جاري فتح: {url}";
            }
            else if (aiResponse.StartsWith("ACTION:SEARCH:"))
            {
                string query = aiResponse.Replace("ACTION:SEARCH:", "").Trim();
                string searchUrl = $"https://www.google.com/search?q={Uri.EscapeDataString(query)}";
                Dispatcher.Invoke(() => {
                    UrlTextBox.Text = searchUrl;
                    MainWebView.CoreWebView2.Navigate(searchUrl);
                });
                return $"🔍 جاري البحث عن: {query}";
            }
            else if (aiResponse.StartsWith("ACTION:REPLY:"))
            {
                return aiResponse.Replace("ACTION:REPLY:", "").Trim();
            }
            return aiResponse;
        }

        private void AddMessage(string message, bool isUser, bool isError = false)
        {
            var border = new Border
            {
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10),
                Margin = new Thickness(5),
                MaxWidth = 280,
                Background = isError
                    ? new SolidColorBrush(Color.FromRgb(180, 50, 50))
                    : isUser
                        ? new SolidColorBrush(Color.FromRgb(0, 122, 204))
                        : new SolidColorBrush(Color.FromRgb(60, 60, 60)),
                HorizontalAlignment = isUser ? HorizontalAlignment.Right : HorizontalAlignment.Left
            };

            var text = new TextBlock
            {
                Text = message,
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13
            };

            border.Child = text;
            ChatMessages.Children.Add(border);
            ChatScroller.ScrollToBottom();
        }

        private void ClearChat_Click(object sender, RoutedEventArgs e)
        {
            ChatMessages.Children.Clear();
            SetStatus("🟢 تم مسح المحادثة");
        }

        private void SetStatus(string status)
        {
            Dispatcher.Invoke(() => StatusText.Text = status);
        }

        private void ToggleChat_Click(object sender, RoutedEventArgs e)
        {
            if (ChatPanel.Visibility == Visibility.Visible)
            {
                ChatPanel.Visibility = Visibility.Collapsed;
                ChatColumnDefinition.Width = new GridLength(0);
            }
            else
            {
                ChatPanel.Visibility = Visibility.Visible;
                ChatColumnDefinition.Width = new GridLength(350);
            }
        }

        private void Go_Click(object sender, RoutedEventArgs e)
        {
            string url = UrlTextBox.Text;
            if (!url.StartsWith("http://") && !url.StartsWith("https://"))
            {
                url = "https://" + url;
                UrlTextBox.Text = url;
            }
            if (MainWebView?.CoreWebView2 != null)
                MainWebView.CoreWebView2.Navigate(url);
        }
    }
}
