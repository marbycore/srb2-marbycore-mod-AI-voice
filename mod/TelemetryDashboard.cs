using System;
using System.Drawing;
using System.Windows.Forms;
using System.IO;
using System.Net;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Media;
using NAudio.Wave;

namespace SRB2Dashboard
{
    public class Dashboard : Form
    {
        // Settings
        private string logPath = @"bin\VC10\Win32\Release\latest-log.txt";
        private long lastFileSize = 0;
        private System.Windows.Forms.Timer updateTimer;
        private System.Windows.Forms.Timer strategyTimer;
        
        // Data State
        private Dictionary<string, string> telemetryData = new Dictionary<string, string>();
        
        // Game Modes
        private enum GameMode { Default, Chat, Quiz }
        private GameMode currentMode = GameMode.Default;

        // Global Hook
        private static LowLevelKeyboardProc _staticProc; // Keep-alive static reference
        private LowLevelKeyboardProc _proc;
        private IntPtr _hookID = IntPtr.Zero;
        private bool isListening = false;
        private bool isProcessing = false; // Prevents overlapping sessions
        
        // Audio Recording (NAudio)
        private WaveInEvent waveIn;
        private WaveFileWriter waveWriter;
        private MemoryStream audioStream;
        private float peakVolume = 0f;
        
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int VK_C = 0x43;
        
        // Strategy Logic
        private int currentStrategyIndex = 0;
        private List<ComboBox> strategySlots = new List<ComboBox>();
        private List<Panel> strategyPanels = new List<Panel>();
        private string[] strategyTypes = new string[] { 
            LanguageManager.GetUIVitals(), 
            LanguageManager.GetUINav(), 
            LanguageManager.GetUIThreat(), 
            LanguageManager.CurrentLanguage == LanguageManager.AppLanguage.Spanish ? "MIXTO (Mixed)" : "MIXED (Mixto)", 
            LanguageManager.CurrentLanguage == LanguageManager.AppLanguage.Spanish ? "HUMOR (Random)" : "HUMOR (Random)", 
            LanguageManager.CurrentLanguage == LanguageManager.AppLanguage.Spanish ? "SILENCIO (Skip)" : "SILENCE (Skip)" 
        };
        private bool isStrategyPaused = false;
        private Button btnPauseStrategy;

        // UI Controls
        private Dictionary<string, Label> statsLabels = new Dictionary<string, Label>();
        private TextBox txtPromptPreview;
        private TextBox txtLog;
        private TextBox txtLevelInfo;
        private TextBox txtGeneralContext;
        private Dictionary<string, string> levelLore = new Dictionary<string, string>();
        private bool ttsEnabled = true;
        private Button btnTTS;
        private Button btnModeDefault;
        private Button btnModeChat;
        private Button btnModeQuiz;
        private TextBox txtChatInput;
        private Button btnSendChat;
        private TextBox txtFullPrompt; 
        private TextBox txtSnapshotPreview; // New box for live telemetry
        private Panel pnlCommandList; // UI List for voice commands
        private List<TextBox> phraseInputs = new List<TextBox>(); // Track inputs for saving
        private List<TextBox> descriptionInputs = new List<TextBox>(); // Track Tails' responses

        // Profile System
        private ComboBox cmbProfiles;
        private ComboBox cmbMics;
        private ProgressBar pbVolume;
        private Button btnSaveProfile;
        private Button btnDeleteProfile;
        private CheckBox chkIsDefault;
        private Label lblProf;
        private ComboBox cmbAIProvider;
        private TextBox txtAIBaseUrl;
        private TextBox txtAIApiKey;
        private TextBox txtAIModel;
        private Button btnSaveAISettings;
        private Button btnTestConnection;
        private bool isProgrammaticUpdate = false;

        private string chatProfilesPath = @"mod\prompt_profiles.json";
        private string missionProfilesPath = @"mod\mission_profiles.json";
        private string aiProviderSettingsPath = @"mod\ai_provider_settings.json";
        private Dictionary<string, Dictionary<string, string>> providerConfigs = new Dictionary<string, Dictionary<string, string>>();
        
        // Microphone Listener Provider System
        private string micListenerSettingsPath = @"mod\mic_listener_settings.json";
        private Dictionary<string, Dictionary<string, string>> listenerConfigs = new Dictionary<string, Dictionary<string, string>>();
        private string currentListenerProvider = "Whisper Local";
        private ComboBox cmbMicListenerProvider;
        private TextBox txtListenerApiKey;
        private TextBox txtListenerModel;
        private List<PromptProfile> chatProfiles = new List<PromptProfile>();
        private List<PromptProfile> missionProfiles = new List<PromptProfile>();

        // TTS Provider System
        private string ttsSettingsPath = @"mod\tts_settings.json";
        private Dictionary<string, Dictionary<string, string>> ttsConfigs = new Dictionary<string, Dictionary<string, string>>();
        private TTSProvider ttsProvider = new TTSProvider();
        private ComboBox cmbTTSProvider;
        private TextBox txtTTSApiKey;
        private TextBox txtTTSModel;
        private ComboBox cmbLanguage;

        // Helper to get active context
        private List<PromptProfile> ActiveProfiles {
            get { return (currentMode == GameMode.Chat) ? chatProfiles : missionProfiles; }
        }
        private string ActiveProfilePath {
            get { return (currentMode == GameMode.Chat) ? chatProfilesPath : missionProfilesPath; }
        }
        // Quiz System
        private string quizPath = @"mod\quiz_data.json";

        public class QuizItem {
            public string Question { get; set; }
            public List<string> Answers { get; set; }
            public string Reward { get; set; }
            public QuizItem() {
                Reward = "Rings";
                Answers = new List<string>();
            }
        }
        private List<QuizItem> quizItems = new List<QuizItem>();
        private List<TextBox> quizQuestionsUI = new List<TextBox>();
        private List<TextBox> quizAnswersUI = new List<TextBox>();
        private List<ComboBox> quizRewardsUI = new List<ComboBox>();
        private Panel pnlQuizRoot;

        public class PromptProfile {
            public string Name { get; set; }
            public string Template { get; set; }
            public bool IsDefault { get; set; }
        }

        public Dashboard()
        {
            LanguageManager.Load();
            string welcome = LanguageManager.CurrentLanguage == LanguageManager.AppLanguage.Spanish 
                ? "DASHBOARD VOZ ACTIVADO v28.4 [MEJORAS IDIOMA]" 
                : "VOICE DASHBOARD ACTIVATED v28.4 [LANGUAGE IMPROVEMENTS]";
            MessageBox.Show(welcome);
            
            this.Text = LanguageManager.CurrentLanguage == LanguageManager.AppLanguage.Spanish 
                ? "SRB2 AI COMMANDER - v28.4" 
                : "SRB2 AI COMMANDER (EN) - v28.4";

            // Re-initialize localized strings
            strategyTypes = new string[] { 
                LanguageManager.GetUIVitals(), 
                LanguageManager.GetUINav(), 
                LanguageManager.GetUIThreat(), 
                LanguageManager.CurrentLanguage == LanguageManager.AppLanguage.Spanish ? "MIXTO (Mixed)" : "MIXED (Mixto)", 
                LanguageManager.CurrentLanguage == LanguageManager.AppLanguage.Spanish ? "HUMOR (Random)" : "HUMOR (Random)", 
                LanguageManager.CurrentLanguage == LanguageManager.AppLanguage.Spanish ? "SILENCIO (Skip)" : "SILENCE (Skip)" 
            };
            this.Size = new Size(1100, 950);
            this.AutoScroll = true;
            this.BackColor = Color.FromArgb(25, 25, 25);
            this.ForeColor = Color.White;
            this.Font = new Font("Segoe UI", 9F);

            LoadLevelLore();
            LoadProviderSettings();
            LoadListenerSettings();
            LoadTTSSettings();
            ttsProvider.OnLog += (msg) => AppendLog(msg);
            InitializeUI();
            
            _proc = HookCallback;
            _staticProc = _proc; // Ensure static ref to prevent GC
            _hookID = SetHook(_staticProc);

            this.FormClosing += (s, e) => {
                UnhookWindowsHookEx(_hookID);
            };
            
            updateTimer = new System.Windows.Forms.Timer();
            updateTimer.Interval = 100; 
            updateTimer.Tick += (s, e) => RequestTelemetry();
            updateTimer.Start();

            strategyTimer = new System.Windows.Forms.Timer();
            strategyTimer.Interval = 22000;
            strategyTimer.Tick += (s, e) => AdvanceStrategy();
            strategyTimer.Start();
            ListAudioDevices();
        }

        private void ListAudioDevices()
        {
            try {
                AppendLog("--- DISPOSITIVOS DE AUDIO ---");
                cmbMics.Items.Clear();
                for (int i = 0; i < WaveIn.DeviceCount; i++)
                {
                    var caps = WaveIn.GetCapabilities(i);
                    string name = caps.ProductName;
                    AppendLog(string.Format("Mic {0}: {1}", i, name));
                    cmbMics.Items.Add(string.Format("{0}: {1}", i, name));
                }
                if (cmbMics.Items.Count > 0) cmbMics.SelectedIndex = 0;
                AppendLog("-----------------------------");
            } catch { }
        }

        private string BuildProviderJson(string prompt, bool useChatAPI, string model)
        {
            string escapedPrompt = prompt.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
            string stopTokens = "[\"<|im_end|>\", \"<|im_start|>\", \"<|endoftext|>\", \"User:\", \"Assistant:\"]";

            if (useChatAPI) {
                string modelField = string.IsNullOrEmpty(model) ? "deepseek-chat" : model;
                return string.Format("{{ \"model\": \"{0}\", \"messages\": [{{ \"role\": \"user\", \"content\": \"{1}\" }}], \"max_tokens\": 512, \"temperature\": 0.7, \"stop\": {2} }}", modelField, escapedPrompt, stopTokens);
            } else {
                return string.Format("{{ \"prompt\": \"{0}\", \"stop\": {1}, \"max_tokens\": 512, \"temperature\": 0.7 }}", escapedPrompt, stopTokens);
            }
        }

        private string CleanAIResponse(string aiText)
        {
            if (string.IsNullOrEmpty(aiText)) return "";

            // 1. Robust removal of ALL <think> tags and their content
            aiText = System.Text.RegularExpressions.Regex.Replace(aiText, @"<think>.*?</think>", "", System.Text.RegularExpressions.RegexOptions.Singleline);
            
            // 2. Remove ANY lingering XML/HTML-like tags
            aiText = System.Text.RegularExpressions.Regex.Replace(aiText, @"<[^>]+>", ""); 

            // 3. Remove AI prefixes
            string[] prefixes = { "Tails:", "Assistant:", "User:", "AI:", "Respuesta:", "System:", "Narrador:" };
            foreach (var p in prefixes) {
                if (aiText.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                    aiText = aiText.Substring(p.Length).Trim();
            }
            aiText = aiText.Replace("_i", "").Replace("\"", "").Trim();

            // 4. Process common escapes and newlines
            aiText = aiText.Replace("\\n", " ").Replace("\n", " ").Replace("\\r", "").Replace("\r", "").Replace("\\\"", "\"").Trim();
            
            // 5. Cleanup for HUD (Mojibake compatibility)
            aiText = System.Text.RegularExpressions.Regex.Replace(aiText, @"[^\w\s├▒├í├®├¡├│├║├ü├ë├ì├ô├Ü!,┬┐?┬í\.:\(\)-]", "");

            // 6. Final trim and limit
            aiText = aiText.Trim();
            if (aiText.Length > 280) aiText = aiText.Substring(0, 277) + "...";

            return aiText;
        }

        private string ExtractAIText(string result, bool useChatAPI)
        {
            if (useChatAPI) {
                int contentIdx = result.IndexOf("\"content\"", result.IndexOf("\"assistant\"") > 0 ? result.IndexOf("\"assistant\"") : 0);
                if (contentIdx == -1) contentIdx = result.IndexOf("\"content\"");
                if (contentIdx != -1) {
                    int colonIdx = result.IndexOf(":", contentIdx);
                    if (colonIdx != -1) {
                        int startQuote = result.IndexOf("\"", colonIdx);
                        if (startQuote != -1) {
                            int endQuote = startQuote + 1;
                            while (endQuote < result.Length) {
                                endQuote = result.IndexOf("\"", endQuote);
                                if (endQuote == -1) break;
                                if (result[endQuote - 1] != '\\') break;
                                endQuote++;
                            }
                            if (endQuote != -1 && endQuote > startQuote) {
                                return result.Substring(startQuote + 1, endQuote - startQuote - 1).Replace("\\n", "\n").Replace("\\\"", "\"");
                            }
                        }
                    }
                }
            } else {
                int textKeyIdx = result.IndexOf("\"text\"");
                if (textKeyIdx != -1) {
                    int colonIdx = result.IndexOf(":", textKeyIdx);
                    if (colonIdx != -1) {
                        int startQuote = result.IndexOf("\"", colonIdx);
                        if (startQuote != -1) {
                            int endQuote = result.IndexOf("\"", startQuote + 1);
                            while (endQuote != -1 && result[endQuote - 1] == '\\') {
                                endQuote = result.IndexOf("\"", endQuote + 1);
                            }
                            if (endQuote != -1) {
                                return result.Substring(startQuote + 1, endQuote - startQuote - 1).Replace("\\n", "\n").Replace("\\\"", "\"");
                            }
                        }
                    }
                }
            }
            return "";
        }

        private void LoadProviderSettings()
        {
            // Hardcoded Defaults (Ensures they always appear in the UI)
            providerConfigs["LM Studio"] = new Dictionary<string, string> {
                { "base_url", "http://127.0.0.1:1234" },
                { "api_key", "" },
                { "model", "" }
            };
            providerConfigs["DeepSeek"] = new Dictionary<string, string> {
                { "base_url", "https://api.deepseek.com/v1" },
                { "api_key", "YOUR_API_KEY_HERE" },
                { "model", "deepseek-chat" }
            };
            providerConfigs["OpenRouter"] = new Dictionary<string, string> {
                { "base_url", "https://openrouter.ai/api/v1" },
                { "api_key", "YOUR_API_KEY_HERE" },
                { "model", "google/gemini-2.0-flash-001" }
            };

            if (File.Exists(aiProviderSettingsPath)) {
                try {
                    string json = File.ReadAllText(aiProviderSettingsPath);
                    string[] sections = { "LM Studio", "DeepSeek", "OpenRouter" };
                    foreach (var p in sections) {
                        int pIdx = json.IndexOf("\"provider\": \"" + p + "\"");
                        if (pIdx != -1) {
                            int endIdx = json.IndexOf("}", pIdx);
                            if (endIdx != -1) {
                                string block = json.Substring(pIdx, endIdx - pIdx);
                                if (!providerConfigs.ContainsKey(p)) providerConfigs[p] = new Dictionary<string, string>();
                                
                                string bUrl = ExtractJsonValue(block, "base_url");
                                string aKey = ExtractJsonValue(block, "api_key");
                                string mod = ExtractJsonValue(block, "model");
                                
                                if (!string.IsNullOrEmpty(bUrl)) providerConfigs[p]["base_url"] = bUrl;
                                if (!string.IsNullOrEmpty(aKey)) providerConfigs[p]["api_key"] = aKey;
                                if (!string.IsNullOrEmpty(mod)) providerConfigs[p]["model"] = mod;
                            }
                        }
                    }
                } catch { }
            }
        }

        private void LoadAIProviderIntoUI()
        {
            if (cmbAIProvider.SelectedItem == null) return;
            string provider = cmbAIProvider.SelectedItem.ToString();
            
            if (providerConfigs.ContainsKey(provider)) {
                var cfg = providerConfigs[provider];
                txtAIBaseUrl.Text = cfg.ContainsKey("base_url") ? cfg["base_url"] : "";
                txtAIApiKey.Text = cfg.ContainsKey("api_key") ? cfg["api_key"] : "";
                txtAIModel.Text = cfg.ContainsKey("model") ? cfg["model"] : "";
                
                // Visual feedback for provider type
                if (provider == "LM Studio") cmbAIProvider.ForeColor = Color.LimeGreen;
                else if (provider == "DeepSeek") cmbAIProvider.ForeColor = Color.Cyan;
                else cmbAIProvider.ForeColor = Color.Gold;
            }
        }

        private void SaveAISettings()
        {
            if (cmbAIProvider.SelectedItem == null) return;
            string provider = cmbAIProvider.SelectedItem.ToString();
            
            if (!providerConfigs.ContainsKey(provider)) providerConfigs[provider] = new Dictionary<string, string>();
            
            providerConfigs[provider]["base_url"] = txtAIBaseUrl.Text.Trim();
            providerConfigs[provider]["api_key"] = txtAIApiKey.Text.Trim();
            providerConfigs[provider]["model"] = txtAIModel.Text.Trim();
            
            SaveProvidersToDisk();
            AppendLog("AJUSTES DE " + provider.ToUpper() + " GUARDADOS.");
        }

        private void SaveProvidersToDisk()
        {
            try {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("[");
                var keys = providerConfigs.Keys.ToArray();
                for (int i = 0; i < keys.Length; i++) {
                    var k = keys[i];
                    var cfg = providerConfigs[k];
                    sb.Append("  { \"provider\": \"").Append(k).Append("\", ");
                    sb.Append("\"base_url\": \"").Append(cfg["base_url"]).Append("\", ");
                    sb.Append("\"api_key\": \"").Append(cfg["api_key"]).Append("\", ");
                    sb.Append("\"model\": \"").Append(cfg["model"]).Append("\" }");
                    if (i < keys.Length - 1) sb.Append(",");
                    sb.AppendLine();
                }
                sb.AppendLine("]");
                File.WriteAllText(aiProviderSettingsPath, sb.ToString());
            } catch (Exception ex) {
                AppendLog("ERR GUARDANDO AI: " + ex.Message);
            }
        }

        private void TestAIConnection()
        {
            if (cmbAIProvider.SelectedItem == null) return;
            string provider = cmbAIProvider.SelectedItem.ToString();
            AppendLog("--- TESTEANDO CONEXIÓN: " + provider + " ---");
            
            // Save current UI to temporary config for the test
            string testBase = txtAIBaseUrl.Text.Trim();
            string testKey = txtAIApiKey.Text.Trim();
            string testModel = txtAIModel.Text.Trim();
            
            System.Threading.ThreadPool.QueueUserWorkItem((_) => {
                try {
                    bool useChatAPI = (provider == "DeepSeek" || provider == "ChatGPT" || provider == "OpenRouter");
                    string finalBase = (testBase ?? "").TrimEnd('/');
                    if (useChatAPI && !finalBase.EndsWith("/v1")) finalBase += "/v1";
                    string url = useChatAPI ? finalBase + "/chat/completions" : finalBase + "/v1/completions";

                    var request = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
                    request.Method = "POST";
                    request.ContentType = "application/json";
                    request.Accept = "application/json";
                    request.Timeout = 10000;

                    if (url.ToLower().StartsWith("https")) {
                        System.Net.ServicePointManager.SecurityProtocol = (System.Net.SecurityProtocolType)3072 | (System.Net.SecurityProtocolType)12288;
                        request.Headers.Add("HTTP-Referer", "http://localhost:1234");
                        request.Headers.Add("X-Title", "SRB2 Telemetry Test");
                    }

                    if (!string.IsNullOrEmpty(testKey)) {
                        request.Headers.Add("Authorization", "Bearer " + testKey.Trim());
                    }

                    string testPrompt = "Responde solo con la palabra OK si recibes esto.";
                    string json = BuildProviderJson(testPrompt, useChatAPI, testModel);
                    byte[] bytes = System.Text.Encoding.UTF8.GetBytes(json);
                    request.ContentLength = bytes.Length;

                    using (var stream = request.GetRequestStream()) {
                        stream.Write(bytes, 0, bytes.Length);
                    }

                    using (var response = (System.Net.HttpWebResponse)request.GetResponse())
                    using (var reader = new System.IO.StreamReader(response.GetResponseStream())) {
                        string result = reader.ReadToEnd();
                        string aiText = ExtractAIText(result, useChatAPI);
                        this.Invoke(new Action(() => {
                            AppendLog("TEST " + provider + ": " + (string.IsNullOrWhiteSpace(aiText) ? "Error al extraer" : aiText));
                            MessageBox.Show("¡Conexión Exitosa!\n\nRespuesta: " + aiText, "Test AI: " + provider);
                        }));
                    }
                } catch (Exception ex) {
                    this.Invoke(new Action(() => {
                        AppendLog("TEST ERR: " + ex.Message);
                        MessageBox.Show("Error de Conexión:\n" + ex.Message, "Test AI fallido");
                    }));
                }
            });
        }

        // === MICROPHONE LISTENER PROVIDER SYSTEM ===
        private void LoadListenerSettings()
        {
            // Hardcoded Defaults
            listenerConfigs["Whisper Local"] = new Dictionary<string, string> {
                { "base_url", "http://localhost:18888" },
                { "api_key", "" },
                { "model", "" }
            };
            listenerConfigs["Groq"] = new Dictionary<string, string> {
                { "base_url", "https://api.groq.com/openai/v1" },
                { "api_key", "YOUR_GROQ_KEY_HERE" },
                { "model", "whisper-large-v3-turbo" }
            };

            // Load saved settings from JSON
            if (File.Exists(micListenerSettingsPath)) {
                try {
                    string json = File.ReadAllText(micListenerSettingsPath);
                    string[] sections = { "Whisper Local", "Groq" };
                    foreach (var p in sections) {
                        int pIdx = json.IndexOf("\"provider\": \"" + p + "\"");
                        if (pIdx != -1) {
                            int endIdx = json.IndexOf("}", pIdx);
                            if (endIdx != -1) {
                                string block = json.Substring(pIdx, endIdx - pIdx);
                                if (!listenerConfigs.ContainsKey(p)) listenerConfigs[p] = new Dictionary<string, string>();
                                
                                string bUrl = ExtractJsonValue(block, "base_url");
                                string aKey = ExtractJsonValue(block, "api_key");
                                string mod = ExtractJsonValue(block, "model");
                                
                                if (!string.IsNullOrEmpty(bUrl)) listenerConfigs[p]["base_url"] = bUrl;
                                if (!string.IsNullOrEmpty(aKey)) listenerConfigs[p]["api_key"] = aKey;
                                if (!string.IsNullOrEmpty(mod)) listenerConfigs[p]["model"] = mod;
                            }
                        }
                    }
                    // Load current selected provider
                    int provIdx = json.IndexOf("\"current_provider\":\"");
                    if (provIdx != -1) {
                        int start = provIdx + 20;
                        int end = json.IndexOf("\"", start);
                        if (end > start) {
                            currentListenerProvider = json.Substring(start, end - start);
                        }
                    }
                } catch { }
            }
        }

        private void LoadListenerIntoUI()
        {
            if (cmbMicListenerProvider.SelectedItem == null) return;
            string provider = cmbMicListenerProvider.SelectedItem.ToString();
            currentListenerProvider = provider;
            
            if (listenerConfigs.ContainsKey(provider)) {
                var cfg = listenerConfigs[provider];
                txtListenerApiKey.Text = cfg.ContainsKey("api_key") ? cfg["api_key"] : "";
                txtListenerModel.Text = cfg.ContainsKey("model") ? cfg["model"] : "";
                
                // Visual feedback for provider type
                if (provider == "Whisper Local") cmbMicListenerProvider.ForeColor = Color.LimeGreen;
                else if (provider == "Groq") cmbMicListenerProvider.ForeColor = Color.Cyan;
            }
        }

        private void SaveListenerSettings()
        {
            if (cmbMicListenerProvider.SelectedItem == null) return;
            string provider = cmbMicListenerProvider.SelectedItem.ToString();
            currentListenerProvider = provider;
            
            if (!listenerConfigs.ContainsKey(provider)) listenerConfigs[provider] = new Dictionary<string, string>();
            
            listenerConfigs[provider]["api_key"] = txtListenerApiKey.Text.Trim();
            listenerConfigs[provider]["model"] = txtListenerModel.Text.Trim();
            
            SaveListenersToDisk();
            AppendLog("AJUSTES DE LISTENER " + provider.ToUpper() + " GUARDADOS.");
        }

        private void SaveListenersToDisk()
        {
            try {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine("  \"current_provider\": \"" + currentListenerProvider + "\",");
                sb.AppendLine("  \"listeners\": [");
                var keys = listenerConfigs.Keys.ToArray();
                for (int i = 0; i < keys.Length; i++) {
                    var k = keys[i];
                    var cfg = listenerConfigs[k];
                    sb.Append("    { \"provider\": \"").Append(k).Append("\", ");
                    sb.Append("\"base_url\": \"").Append(cfg.ContainsKey("base_url") ? cfg["base_url"] : "").Append("\", ");
                    sb.Append("\"api_key\": \"").Append(cfg.ContainsKey("api_key") ? cfg["api_key"] : "").Append("\", ");
                    sb.Append("\"model\": \"").Append(cfg.ContainsKey("model") ? cfg["model"] : "").Append("\" }");
                    if (i < keys.Length - 1) sb.Append(",");
                    sb.AppendLine();
                }
                sb.AppendLine("  ]");
                sb.AppendLine("}");
                File.WriteAllText(micListenerSettingsPath, sb.ToString());
            } catch (Exception ex) {
                AppendLog("ERR GUARDANDO LISTENER: " + ex.Message);
            }
        }

        private void TestListenerConnection()
        {
            if (cmbMicListenerProvider.SelectedItem == null) return;
            string provider = cmbMicListenerProvider.SelectedItem.ToString();
            AppendLog("--- TESTEANDO LISTENER: " + provider + " ---");
            
            if (provider == "Whisper Local") {
                AppendLog("Whisper Local: Asegúrate de que el servidor local esté en http://localhost:18888");
                MessageBox.Show("Whisper Local usa el servidor local en puerto 18888.", "Test Listener");
            } else if (provider == "Groq") {
                string apiKey = txtListenerApiKey.Text.Trim();
                if (string.IsNullOrEmpty(apiKey)) {
                    MessageBox.Show("Por favor ingresa una API Key de Groq.", "Test Listener");
                    return;
                }
                AppendLog("Groq: Probando conexión con modelo " + txtListenerModel.Text);
                MessageBox.Show("Groq connection test: API Key configurada para " + apiKey.Substring(0, Math.Min(8, apiKey.Length)) + "...", "Test Listener");
            }
        }

        // === TTS PROVIDER SYSTEM ===
        private void LoadTTSSettings()
        {
            // Defaults
            ttsConfigs["Local"] = new Dictionary<string, string> {
                { "base_url", "http://127.0.0.1:5000" },
                { "api_key", "" },
                { "model", "" }
            };
            ttsConfigs["HuggingFace"] = new Dictionary<string, string> {
                { "base_url", "https://api-inference.huggingface.co/models/" },
                { "api_key", "YOUR_HF_KEY_HERE" },
                { "model", "coqui/XTTS-v2" }
            };
            ttsConfigs["Piper"] = new Dictionary<string, string> {
                { "base_url", "" },
                { "api_key", "" },
                { "model", "es_AR-daniela-high.onnx" }
            };

            if (File.Exists(ttsSettingsPath)) {
                try {
                    string json = File.ReadAllText(ttsSettingsPath);
                    string[] sections = { "Local", "HuggingFace" };
                    foreach (var p in sections) {
                        int pIdx = json.IndexOf("\"provider\": \"" + p + "\"");
                        if (pIdx != -1) {
                            int endIdx = json.IndexOf("}", pIdx);
                            if (endIdx != -1) {
                                string block = json.Substring(pIdx, endIdx - pIdx);
                                if (!ttsConfigs.ContainsKey(p)) ttsConfigs[p] = new Dictionary<string, string>();
                                
                                string bUrl = ExtractJsonValue(block, "base_url");
                                string aKey = ExtractJsonValue(block, "api_key");
                                string mod = ExtractJsonValue(block, "model");
                                
                                if (!string.IsNullOrEmpty(bUrl)) ttsConfigs[p]["base_url"] = bUrl;
                                if (!string.IsNullOrEmpty(aKey)) ttsConfigs[p]["api_key"] = aKey;
                                if (!string.IsNullOrEmpty(mod)) ttsConfigs[p]["model"] = mod;
                            }
                        }
                    }
                    int provIdx = json.IndexOf("\"current_provider\":\"");
                    if (provIdx != -1) {

                        int start = provIdx + 20;
                        int end = json.IndexOf("\"", start);
                        if (end > start) {
                            string provStr = json.Substring(start, end - start);
                            if (provStr == "HuggingFace") ttsProvider.CurrentProvider = TTSProvider.ProviderType.HuggingFace;
                            else if (provStr == "Piper") ttsProvider.CurrentProvider = TTSProvider.ProviderType.Piper;
                            else ttsProvider.CurrentProvider = TTSProvider.ProviderType.Local;
                        }
                    }
                } catch { }
            }
            
            // Sync provider object with initial config
            string current = "Local";
            if (ttsProvider.CurrentProvider == TTSProvider.ProviderType.HuggingFace) current = "HuggingFace";
            else if (ttsProvider.CurrentProvider == TTSProvider.ProviderType.Piper) current = "Piper";

            if (ttsConfigs.ContainsKey(current)) {
                ttsProvider.BaseUrl = ttsConfigs[current].ContainsKey("base_url") ? ttsConfigs[current]["base_url"] : "";
                ttsProvider.ApiKey = ttsConfigs[current].ContainsKey("api_key") ? ttsConfigs[current]["api_key"] : "";
                ttsProvider.Model = ttsConfigs[current].ContainsKey("model") ? ttsConfigs[current]["model"] : "";
                
                if (current == "Piper") ttsProvider.PiperVoicePath = Path.Combine(@"mod\piper_voices", ttsProvider.Model);
            }
        }

        private void LoadTTSIntoUI()
        {
            if (cmbTTSProvider == null) return;
            if (cmbTTSProvider.SelectedItem == null) {
                if (ttsProvider.CurrentProvider == TTSProvider.ProviderType.HuggingFace)
                    cmbTTSProvider.SelectedItem = "HuggingFace";
                else
                    cmbTTSProvider.SelectedItem = "Local";
            }
            
            string provider = cmbTTSProvider.SelectedItem.ToString();
            if (provider == "HuggingFace") ttsProvider.CurrentProvider = TTSProvider.ProviderType.HuggingFace;
            else if (provider == "Piper") ttsProvider.CurrentProvider = TTSProvider.ProviderType.Piper;
            else ttsProvider.CurrentProvider = TTSProvider.ProviderType.Local;
            
            if (ttsConfigs.ContainsKey(provider)) {
                var cfg = ttsConfigs[provider];
                txtTTSApiKey.Text = cfg.ContainsKey("api_key") ? cfg["api_key"] : "";
                txtTTSModel.Text = cfg.ContainsKey("model") ? cfg["model"] : "";
                
                if (provider == "Local") cmbTTSProvider.ForeColor = Color.HotPink;
                else if (provider == "Piper") cmbTTSProvider.ForeColor = Color.LimeGreen;
                else cmbTTSProvider.ForeColor = Color.Gold;
            }

            // Update provider object in real-time
            ttsProvider.BaseUrl = ttsConfigs[provider].ContainsKey("base_url") ? ttsConfigs[provider]["base_url"] : "";
            ttsProvider.ApiKey = txtTTSApiKey.Text.Trim();
            ttsProvider.Model = txtTTSModel.Text.Trim();

            if (provider == "Piper") {
                ttsProvider.PiperVoicePath = Path.Combine(@"mod\piper_voices", ttsProvider.Model);
            }
        }

        private void SaveTTSSettings()
        {
            if (cmbTTSProvider.SelectedItem == null) return;
            string provider = cmbTTSProvider.SelectedItem.ToString();
            
            if (!ttsConfigs.ContainsKey(provider)) ttsConfigs[provider] = new Dictionary<string, string>();
            
            ttsConfigs[provider]["api_key"] = txtTTSApiKey.Text.Trim();
            ttsConfigs[provider]["model"] = txtTTSModel.Text.Trim();
            
            // Sync provider object
            ttsProvider.ApiKey = ttsConfigs[provider]["api_key"];
            ttsProvider.Model = ttsConfigs[provider]["model"];
            
            SaveTTSToDisk();
            AppendLog("AJUSTES DE TTS " + provider.ToUpper() + " GUARDADOS.");
        }

        private void SaveTTSToDisk()
        {
            try {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("{");
                string currentProvStr = "Local";
                if (ttsProvider.CurrentProvider == TTSProvider.ProviderType.HuggingFace) currentProvStr = "HuggingFace";
                else if (ttsProvider.CurrentProvider == TTSProvider.ProviderType.Piper) currentProvStr = "Piper";

                sb.AppendLine("  \"current_provider\": \"" + currentProvStr + "\",");
                sb.AppendLine("  \"tts_configs\": [");
                var keys = ttsConfigs.Keys.ToArray();
                for (int i = 0; i < keys.Length; i++) {
                    var k = keys[i];
                    var cfg = ttsConfigs[k];
                    sb.Append("    { \"provider\": \"").Append(k).Append("\", ");
                    sb.Append("\"base_url\": \"").Append(cfg.ContainsKey("base_url") ? cfg["base_url"] : "").Append("\", ");
                    sb.Append("\"api_key\": \"").Append(cfg.ContainsKey("api_key") ? cfg["api_key"] : "").Append("\", ");
                    sb.Append("\"model\": \"").Append(cfg.ContainsKey("model") ? cfg["model"] : "").Append("\" }");
                    if (i < keys.Length - 1) sb.Append(",");
                    sb.AppendLine();
                }
                sb.AppendLine("  ]");
                sb.AppendLine("}");
                File.WriteAllText(ttsSettingsPath, sb.ToString());
            } catch (Exception ex) {
                AppendLog("ERR GUARDANDO TTS: " + ex.Message);
            }
        }

        private void InitializeUI()
        {
            LanguageManager.Load();

            // HEADER
            Label lblHeader = CreateLabel("SRB2 AI COMMANDER", 16, FontStyle.Bold, Color.FromArgb(0, 122, 204), new Point(20, 15));
            this.Controls.Add(lblHeader);

            // LANGUAGE SELECTOR (Above Sequencer)
            Label lblLang = CreateLabel("IDIOMA / LANGUAGE:", 7, FontStyle.Bold, Color.Gray, new Point(830, 15));
            this.Controls.Add(lblLang);
            cmbLanguage = new ComboBox { 
                Location = new Point(830, 30), 
                Size = new Size(230, 25), 
                BackColor = Color.FromArgb(30,30,30), 
                ForeColor = Color.White, 
                FlatStyle = FlatStyle.Flat, 
                DropDownStyle = ComboBoxStyle.DropDownList 
            };
            cmbLanguage.Items.Add("English");
            cmbLanguage.Items.Add("Spanish");
            cmbLanguage.SelectedItem = LanguageManager.CurrentLanguage.ToString();
            cmbLanguage.SelectedIndexChanged += (s, e) => {
                LanguageManager.CurrentLanguage = (cmbLanguage.SelectedItem.ToString() == "Spanish") ? LanguageManager.AppLanguage.Spanish : LanguageManager.AppLanguage.English;
                LanguageManager.Save();
                SyncTTSLanguage();
                MessageBox.Show(LanguageManager.CurrentLanguage == LanguageManager.AppLanguage.Spanish ? "Idioma cambiado. Algunos cambios requieren reiniciar." : "Language changed. Some changes require restart.");
            };
            this.Controls.Add(cmbLanguage);
            SyncTTSLanguage();

            // === SCROLLABLE COLUMNS ===
            // Column 1: Vitals (with scroll)
            Panel grpVitals = CreateScrollableGroup(LanguageManager.GetUIVitals(), 20, 60, 250, 300);
            CreateStatCard(grpVitals, "RINGS", 5, 5, 220, Color.Gold);
            CreateStatCard(grpVitals, "VIDAS", 5, 65, 220, Color.LightGreen);
            CreateStatCard(grpVitals, "ESCUDO", 5, 125, 220, Color.LightBlue);
            CreateStatCard(grpVitals, "ESTADO", 5, 185, 220, Color.White);
            CreateStatCard(grpVitals, "GOLPEADO", 5, 245, 220, Color.Red);
            this.Controls.Add(grpVitals);

            // Column 2: Navigation (with scroll)
            Panel grpNav = CreateScrollableGroup(LanguageManager.GetUINav(), 290, 60, 250, 300);
            CreateStatCard(grpNav, "NIVEL", 5, 5, 100);
            CreateStatCard(grpNav, "NOMBRE DE MAPA", 5, 65, 220, Color.Cyan);
            CreateStatCard(grpNav, "OBJETIVO", 5, 125, 220, Color.Cyan);
            CreateStatCard(grpNav, "CHECKPOINT", 5, 185, 220, Color.LightGreen);
            CreateStatCard(grpNav, "TIEMPO", 115, 5, 100);
            CreateStatCard(grpNav, "VELOCIDAD", 5, 245, 220);
            this.Controls.Add(grpNav);

            // Column 3: Environment and Danger (with scroll)
            Panel grpThreat = CreateScrollableGroup(LanguageManager.GetUIThreat(), 560, 60, 250, 300);
            CreateStatCard(grpThreat, "TIPO DE ENEMIGOS", 5, 5, 220, Color.Salmon);
            CreateStatCard(grpThreat, "AMBIENTE", 5, 65, 220, Color.LightBlue);
            CreateStatCard(grpThreat, "PELIGRO DE AHOGARSE", 5, 125, 220, Color.OrangeRed);
            CreateStatCard(grpThreat, "BLOQUEADO", 5, 185, 220, Color.Yellow);
            this.Controls.Add(grpThreat);

            // === STRATEGY SEQUENCER (Right Side) ===
            GroupBox grpStrat = CreateGroup(LanguageManager.GetUISequencer(), 830, 60, 230, 590); // Height increased for Back button
            this.Controls.Add(grpStrat);

            Label lblStratInfo = CreateLabel(LanguageManager.CurrentLanguage == LanguageManager.AppLanguage.Spanish ? "Ciclo de Tópicos:" : "Topic Cycle:", 9, FontStyle.Regular, Color.Gray, new Point(10, 20));
            grpStrat.Controls.Add(lblStratInfo);

            Label lblInt = CreateLabel(LanguageManager.CurrentLanguage == LanguageManager.AppLanguage.Spanish ? "Intervalo:" : "Interval:", 8, FontStyle.Regular, Color.Gray, new Point(10, 45));
            grpStrat.Controls.Add(lblInt);
            
            TrackBar slider = new TrackBar();
            slider.Location = new Point(60, 40);
            slider.Size = new Size(100, 45);
            slider.Minimum = 5;
            slider.Maximum = 120;
            slider.Value = 22;
            slider.TickStyle = TickStyle.None;
            Label lblVal = CreateLabel("22s", 9, FontStyle.Bold, Color.White, new Point(170, 45));
            slider.Scroll += (s, e) => {
                lblVal.Text = slider.Value + "s";
                strategyTimer.Interval = slider.Value * 1000;
            };
            grpStrat.Controls.Add(slider);
            grpStrat.Controls.Add(lblVal);

            // Pause/Resume Button
            btnPauseStrategy = new Button();
            btnPauseStrategy.Text = LanguageManager.GetBtnStop();
            btnPauseStrategy.Location = new Point(10, 85);
            btnPauseStrategy.Size = new Size(210, 30);
            btnPauseStrategy.BackColor = Color.Firebrick;
            btnPauseStrategy.FlatStyle = FlatStyle.Flat;
            btnPauseStrategy.Font = new Font("Segoe UI", 9, FontStyle.Bold);
            btnPauseStrategy.Click += (s, e) => {
                isStrategyPaused = !isStrategyPaused;
                if (isStrategyPaused) {
                    btnPauseStrategy.Text = LanguageManager.GetBtnPlay();
                    btnPauseStrategy.BackColor = Color.ForestGreen;
                } else {
                    btnPauseStrategy.Text = LanguageManager.GetBtnStop();
                    btnPauseStrategy.BackColor = Color.Firebrick;
                }
            };
            grpStrat.Controls.Add(btnPauseStrategy);

            for (int i = 0; i < 7; i++)
            {
                Panel p = new Panel();
                p.Location = new Point(10, 125 + (i * 55)); // Shifted down +45
                p.Size = new Size(210, 50);
                p.BackColor = Color.FromArgb(35, 35, 35);
                p.Padding = new Padding(2);
                
                Label l = CreateLabel((LanguageManager.CurrentLanguage == LanguageManager.AppLanguage.Spanish ? "Paso " : "Step ") + (i + 1), 8, FontStyle.Regular, Color.Gray, new Point(5, 5));
                p.Controls.Add(l);

                ComboBox cb = new ComboBox();
                cb.Location = new Point(5, 22);
                cb.Size = new Size(200, 23);
                cb.Items.AddRange(strategyTypes.Select(s => {
                    if (LanguageManager.CurrentLanguage == LanguageManager.AppLanguage.English) {
                        return s.Replace("NAVEGACIÓN (Guide)", "NAVIGATION (Guide)")
                                .Replace("PELIGROS (Alert)", "HAZARDS (Alert)")
                                .Replace("MIXTO (Mixed)", "MIXED (Ironic)")
                                .Replace("HUMOR (Random)", "HUMOR (Random)")
                                .Replace("VITALES (Status)", "VITALS (Status)")
                                .Replace("SILENCIO (Skip)", "SILENCE (Skip)");
                    }
                    return s;
                }).ToArray());
                cb.DropDownStyle = ComboBoxStyle.DropDownList;
                
                if(i == 0) cb.SelectedIndex = 0; // VITALES
                else if(i == 1) cb.SelectedIndex = 1; // NAVEGACI├ôN
                else if(i == 2) cb.SelectedIndex = 2; // PELIGROS
                else if(i == 3) cb.SelectedIndex = 3; // MIXTO
                else if(i == 4) cb.SelectedIndex = 4; // HUMOR
                else if(i == 5) cb.SelectedIndex = 5; // SILENCIO
                else cb.SelectedIndex = 3; // MIXTO (Repite)

                cb.SelectedIndexChanged += (s, e) => UpdatePreview();

                p.Controls.Add(cb);
                grpStrat.Controls.Add(p);
                
                strategySlots.Add(cb);
                strategyPanels.Add(p);
            }
            
            Button btnNext = new Button();
            btnNext.Text = LanguageManager.GetBtnNext();
            btnNext.Location = new Point(10, 505);
            btnNext.Size = new Size(210, 30);
            btnNext.Click += (s, e) => AdvanceStrategy(true, 1); // Manual forward
            btnNext.BackColor = Color.FromArgb(0, 122, 204);
            btnNext.FlatStyle = FlatStyle.Flat;
            btnNext.Font = new Font("Segoe UI", 8, FontStyle.Bold);
            grpStrat.Controls.Add(btnNext);

            Button btnBack = new Button();
            btnBack.Text = LanguageManager.GetBtnBack();
            btnBack.Location = new Point(10, 545);
            btnBack.Size = new Size(210, 30);
            btnBack.Click += (s, e) => AdvanceStrategy(true, -1); // Manual backward
            btnBack.BackColor = Color.FromArgb(45, 45, 48);
            btnBack.FlatStyle = FlatStyle.Flat;
            btnBack.Font = new Font("Segoe UI", 8, FontStyle.Bold);
            grpStrat.Controls.Add(btnBack);

            // === BOTTOM AREA (now has more space) ===
            GroupBox grpPrev = CreateGroup(LanguageManager.CurrentLanguage == LanguageManager.AppLanguage.Spanish ? "VISTA PREVIA DEL PROMPT (Activo)" : "PROMPT PREVIEW (Active)", 20, 380, 790, 120);
            this.Controls.Add(grpPrev);
            txtPromptPreview = CreateTextBox(10);
            grpPrev.Controls.Add(txtPromptPreview);

            GroupBox grpLog = CreateGroup(LanguageManager.CurrentLanguage == LanguageManager.AppLanguage.Spanish ? "RESPUESTA LM STUDIO" : "LM STUDIO RESPONSE", 20, 510, 790, 150);
            this.Controls.Add(grpLog);
            txtLog = CreateTextBox(9);
            grpLog.Controls.Add(txtLog);

            GroupBox grpInfo = CreateGroup(LanguageManager.CurrentLanguage == LanguageManager.AppLanguage.Spanish ? "INFORMACIÓN DEL NIVEL (Lore Context)" : "LEVEL INFORMATION (Lore Context)", 20, 670, 790, 80);
            this.Controls.Add(grpInfo);
            txtLevelInfo = CreateTextBox(9);
            txtLevelInfo.ForeColor = Color.Cyan;
            grpInfo.Controls.Add(txtLevelInfo);

            GroupBox grpGame = CreateGroup(LanguageManager.CurrentLanguage == LanguageManager.AppLanguage.Spanish ? "CONTEXTO GENERAL DEL JUEGO (System Prompt)" : "GENERAL GAME CONTEXT (System Prompt)", 20, 760, 790, 100);
            this.Controls.Add(grpGame);
            txtGeneralContext = CreateTextBox(9);
            txtGeneralContext.ForeColor = Color.Yellow;
            txtGeneralContext.Text = "Responde con prioridad en lo que te preguntan";
            txtGeneralContext.TextChanged += (s, e) => UpdatePreview();
            grpGame.Controls.Add(txtGeneralContext);

            GroupBox grpFull = CreateGroup(LanguageManager.CurrentLanguage == LanguageManager.AppLanguage.Spanish ? "PLANTILLA DEL PROMPT (Edita aquí - usa [[SNAPSHOT]])" : "PROMPT TEMPLATE (Edit here - use [[SNAPSHOT]])", 20, 870, 790, 250);
            this.Controls.Add(grpFull);
            txtFullPrompt = new TextBox { 
                Multiline = true, 
                ScrollBars = ScrollBars.Vertical, 
                Location = new Point(10, 20), 
                Size = new Size(770, 180), 
                BackColor = Color.FromArgb(45, 45, 48), 
                ForeColor = Color.LightSteelBlue, 
                Font = new Font("Consolas", 8), 
                BorderStyle = BorderStyle.None 
            };
            grpFull.Controls.Add(txtFullPrompt);
            txtFullPrompt.TextChanged += (s, e) => {
                if (!isProgrammaticUpdate)
                {
                    // Update in-memory profile template live so switching tabs doesn't lose progress
                    var list = ActiveProfiles;
                    if (cmbProfiles.SelectedIndex >= 0 && cmbProfiles.SelectedIndex < list.Count)
                    {
                        list[cmbProfiles.SelectedIndex].Template = txtFullPrompt.Text;
                    }
                }
                UpdatePreview();
            };

            // Profile Controls (Moved to bottom of grpFull)
            lblProf = new Label { Text = LanguageManager.CurrentLanguage == LanguageManager.AppLanguage.Spanish ? "PERSONALIDADES:" : "PERSONALITIES:", Location = new Point(10, 212), ForeColor = Color.Gray, AutoSize = true };
            grpFull.Controls.Add(lblProf);

            cmbProfiles = new ComboBox { Location = new Point(110, 210), Size = new Size(300, 23), BackColor = Color.FromArgb(30, 30, 30), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbProfiles.SelectedIndexChanged += (s, e) => LoadSelectedProfile();
            grpFull.Controls.Add(cmbProfiles);

            btnSaveProfile = new Button { Text = LanguageManager.GetBtnSave(), Location = new Point(420, 208), Size = new Size(80, 27), BackColor = Color.FromArgb(0, 122, 204), FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 7, FontStyle.Bold) };
            btnSaveProfile.Click += (s, e) => SaveCurrentProfile();
            grpFull.Controls.Add(btnSaveProfile);

            btnDeleteProfile = new Button { Text = LanguageManager.GetBtnDelete(), Location = new Point(510, 208), Size = new Size(80, 27), BackColor = Color.Firebrick, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 7, FontStyle.Bold) };
            btnDeleteProfile.Click += (s, e) => DeleteSelectedProfile();
            grpFull.Controls.Add(btnDeleteProfile);

            chkIsDefault = new CheckBox { Text = LanguageManager.CurrentLanguage == LanguageManager.AppLanguage.Spanish ? "Es Default" : "Is Default", Location = new Point(600, 212), Size = new Size(100, 23), ForeColor = Color.Gray, AutoSize = true };
            chkIsDefault.CheckedChanged += (s, e) => ToggleDefaultProfile(chkIsDefault.Checked);
            grpFull.Controls.Add(chkIsDefault);

            LoadProfiles();

            // Set a larger enough size for the form to enable scrolling
            this.AutoScrollMinSize = new Size(1100, 1850);

            HighlightActiveStrategy();

            btnTTS = new Button();
            btnTTS.Text = LanguageManager.CurrentLanguage == LanguageManager.AppLanguage.Spanish ? "VOZ (Tails): ON" : "VOICE (Tails): ON";
            btnTTS.Location = new Point(830, 665); // Shifted down for Back button (was 625)
            btnTTS.Size = new Size(230, 40);
            btnTTS.BackColor = Color.ForestGreen;
            btnTTS.FlatStyle = FlatStyle.Flat;
            btnTTS.Font = new Font("Segoe UI", 10, FontStyle.Bold);
            btnTTS.Click += (s, e) => {
                ttsEnabled = !ttsEnabled;
                if (LanguageManager.CurrentLanguage == LanguageManager.AppLanguage.Spanish)
                    btnTTS.Text = ttsEnabled ? "VOZ (Tails): ON" : "VOZ (Tails): OFF";
                else
                    btnTTS.Text = ttsEnabled ? "VOICE (Tails): ON" : "VOICE (Tails): OFF";
                btnTTS.BackColor = ttsEnabled ? Color.ForestGreen : Color.Firebrick;
            };
            this.Controls.Add(btnTTS);

            // === GAME MODES ===
            GroupBox grpModes = CreateGroup(LanguageManager.GetUIGameModes(), 830, 715, 230, 210); // Shifted down (was 675)
            this.Controls.Add(grpModes);

            btnModeDefault = new Button { Text = LanguageManager.CurrentLanguage == LanguageManager.AppLanguage.Spanish ? "MISION (Auto)" : "MISSION (Auto)", Location = new Point(10, 20), Size = new Size(100, 30), BackColor = Color.FromArgb(0, 122, 204), FlatStyle = FlatStyle.Flat };
            btnModeChat = new Button { Text = LanguageManager.CurrentLanguage == LanguageManager.AppLanguage.Spanish ? "CHAT (Voz)" : "CHAT (Voice)", Location = new Point(120, 20), Size = new Size(100, 30), BackColor = Color.FromArgb(45, 45, 48), FlatStyle = FlatStyle.Flat };
            btnModeQuiz = new Button { Text = LanguageManager.CurrentLanguage == LanguageManager.AppLanguage.Spanish ? "QUIZ (Reto)" : "QUIZ (Challenge)", Location = new Point(10, 55), Size = new Size(210, 30), BackColor = Color.FromArgb(45, 45, 48), FlatStyle = FlatStyle.Flat };

            btnModeDefault.Click += (s, e) => SetGameMode(GameMode.Default);
            btnModeChat.Click += (s, e) => SetGameMode(GameMode.Chat);
            btnModeQuiz.Click += (s, e) => SetGameMode(GameMode.Quiz);

            grpModes.Controls.Add(btnModeDefault);
            grpModes.Controls.Add(btnModeChat);
            grpModes.Controls.Add(btnModeQuiz);

            txtChatInput = new TextBox { Location = new Point(10, 95), Size = new Size(150, 23), BackColor = Color.FromArgb(30,30,30), ForeColor = Color.White };
            btnSendChat = new Button { Text = LanguageManager.CurrentLanguage == LanguageManager.AppLanguage.Spanish ? "ENVIAR" : "SEND", Location = new Point(165, 93), Size = new Size(55, 27), BackColor = Color.FromArgb(0, 122, 204), FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 7, FontStyle.Bold) };
            btnSendChat.Click += (s, e) => {
                if (!string.IsNullOrWhiteSpace(txtChatInput.Text)) {
                    SendManualChat(txtChatInput.Text);
                    txtChatInput.Clear();
                }
            };
            grpModes.Controls.Add(txtChatInput);
            grpModes.Controls.Add(btnSendChat);

            Label lblMic = new Label { Text = "MIC:", Location = new Point(10, 130), ForeColor = Color.Gray, AutoSize = true };
            cmbMics = new ComboBox { Location = new Point(45, 127), Size = new Size(175, 23), BackColor = Color.FromArgb(30,30,30), ForeColor = Color.Cyan, FlatStyle = FlatStyle.Flat, DropDownStyle = ComboBoxStyle.DropDownList };
            
            pbVolume = new ProgressBar { 
                Location = new Point(10, 160), 
                Size = new Size(210, 10), 
                Style = ProgressBarStyle.Continuous, 
                BackColor = Color.FromArgb(30,30,30),
                ForeColor = Color.LimeGreen
            };

            grpModes.Controls.Add(lblMic);
            grpModes.Controls.Add(cmbMics);
            grpModes.Controls.Add(pbVolume);

            // Re-adjust grpModes size
            grpModes.Height = 180;

            // === AI CONFIG PANEL ===
            GroupBox grpAIControl = CreateGroup(LanguageManager.GetUIAIControl(), 830, 905, 230, 250); // Shifted down (was 865)
            grpAIControl.ForeColor = Color.Gold;
            this.Controls.Add(grpAIControl);

            Label lblAI = new Label { Text = LanguageManager.CurrentLanguage == LanguageManager.AppLanguage.Spanish ? "PERSONALIDAD AI:" : "AI PERSONALITY:", Location = new Point(10, 20), ForeColor = Color.LightGray, Font = new Font("Segoe UI", 7, FontStyle.Bold), AutoSize = true };
            cmbAIProvider = new ComboBox { 
                Location = new Point(10, 35), 
                Size = new Size(210, 28), 
                BackColor = Color.FromArgb(30,30,30), 
                ForeColor = Color.Gold, 
                FlatStyle = FlatStyle.Flat, 
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 10, FontStyle.Bold)
            };
            foreach (var pName in providerConfigs.Keys) cmbAIProvider.Items.Add(pName);
            cmbAIProvider.SelectedIndexChanged += (s, e) => LoadAIProviderIntoUI();
            grpAIControl.Controls.Add(lblAI);
            grpAIControl.Controls.Add(cmbAIProvider);

            Label lblUrl = CreateLabel(LanguageManager.CurrentLanguage == LanguageManager.AppLanguage.Spanish ? "URL Punto Final:" : "Endpoint URL:", 7, FontStyle.Regular, Color.Gray, new Point(10, 70));
            txtAIBaseUrl = new TextBox { Location = new Point(10, 85), Size = new Size(210, 20), BackColor = Color.FromArgb(40,40,40), ForeColor = Color.White, Font = new Font("Consolas", 8), BorderStyle = BorderStyle.FixedSingle };
            
            Label lblKey = CreateLabel("API Key / Secreto:", 7, FontStyle.Regular, Color.Gray, new Point(10, 110));
            txtAIApiKey = new TextBox { Location = new Point(10, 125), Size = new Size(210, 20), BackColor = Color.FromArgb(40,40,40), ForeColor = Color.Gold, PasswordChar = '*', Font = new Font("Consolas", 8), BorderStyle = BorderStyle.FixedSingle };
            
            Label lblModel = CreateLabel(LanguageManager.CurrentLanguage == LanguageManager.AppLanguage.Spanish ? "Modelo Activo:" : "Active Model:", 7, FontStyle.Regular, Color.Gray, new Point(10, 150));
            txtAIModel = new TextBox { Location = new Point(10, 165), Size = new Size(210, 20), BackColor = Color.FromArgb(40,40,40), ForeColor = Color.Cyan, Font = new Font("Consolas", 8), BorderStyle = BorderStyle.FixedSingle };
            
            Button btnSaveAI = new Button { Text = LanguageManager.GetBtnSave() + " PERFIL", Location = new Point(10, 200), Size = new Size(100, 35), BackColor = Color.FromArgb(45, 45, 48), FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 7, FontStyle.Bold), ForeColor = Color.White };
            btnSaveAI.Click += (s, e) => SaveAISettings();

            Button btnTestAI = new Button { Text = "TEST ┐ (tails)", Location = new Point(120, 200), Size = new Size(100, 35), BackColor = Color.FromArgb(0, 122, 204), FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 7, FontStyle.Bold), ForeColor = Color.White };
            btnTestAI.Click += (s, e) => TestAIConnection();

            grpAIControl.Controls.Add(lblUrl); grpAIControl.Controls.Add(txtAIBaseUrl);
            grpAIControl.Controls.Add(lblKey); grpAIControl.Controls.Add(txtAIApiKey);
            grpAIControl.Controls.Add(lblModel); grpAIControl.Controls.Add(txtAIModel);
            grpAIControl.Controls.Add(btnSaveAI);
            grpAIControl.Controls.Add(btnTestAI);

            // Set Initial Selection
            if (cmbAIProvider.Items.Contains("LM Studio")) cmbAIProvider.SelectedItem = "LM Studio";
            else if (cmbAIProvider.Items.Count > 0) cmbAIProvider.SelectedIndex = 0;
            LoadAIProviderIntoUI();

            // === MICROPHONE LISTENER PROVIDER PANEL ===
            GroupBox grpListenerControl = CreateGroup(LanguageManager.GetUIListenerControl(), 830, 1170, 230, 200); // Shifted down (was 1130)
            grpListenerControl.ForeColor = Color.Cyan;
            this.Controls.Add(grpListenerControl);

            Label lblListener = new Label { Text = LanguageManager.CurrentLanguage == LanguageManager.AppLanguage.Spanish ? "PROVEEDOR DE TRANSCRIPCIÓN:" : "TRANSCRIPTION PROVIDER:", Location = new Point(10, 20), ForeColor = Color.LightGray, Font = new Font("Segoe UI", 7, FontStyle.Bold), AutoSize = true };
            cmbMicListenerProvider = new ComboBox { 
                Location = new Point(10, 35), 
                Size = new Size(210, 28), 
                BackColor = Color.FromArgb(30,30,30), 
                ForeColor = Color.LimeGreen, 
                FlatStyle = FlatStyle.Flat, 
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 10, FontStyle.Bold)
            };
            foreach (var pName in listenerConfigs.Keys) cmbMicListenerProvider.Items.Add(pName);
            cmbMicListenerProvider.SelectedIndexChanged += (s, e) => LoadListenerIntoUI();
            grpListenerControl.Controls.Add(lblListener);
            grpListenerControl.Controls.Add(cmbMicListenerProvider);

            Label lblListenerKey = CreateLabel("API Key (Groq):", 7, FontStyle.Regular, Color.Gray, new Point(10, 70));
            txtListenerApiKey = new TextBox { Location = new Point(10, 85), Size = new Size(210, 20), BackColor = Color.FromArgb(40,40,40), ForeColor = Color.Gold, PasswordChar = '*', Font = new Font("Consolas", 8), BorderStyle = BorderStyle.FixedSingle };
            
            Label lblListenerModel = CreateLabel(LanguageManager.CurrentLanguage == LanguageManager.AppLanguage.Spanish ? "Modelo:" : "Model:", 7, FontStyle.Regular, Color.Gray, new Point(10, 110));
            txtListenerModel = new TextBox { Location = new Point(10, 125), Size = new Size(210, 20), BackColor = Color.FromArgb(40,40,40), ForeColor = Color.Cyan, Font = new Font("Consolas", 8), BorderStyle = BorderStyle.FixedSingle };
            
            Button btnSaveListener = new Button { Text = LanguageManager.GetBtnSave(), Location = new Point(10, 155), Size = new Size(100, 30), BackColor = Color.FromArgb(45, 45, 48), FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 7, FontStyle.Bold), ForeColor = Color.White };
            btnSaveListener.Click += (s, e) => SaveListenerSettings();

            Button btnTestListener = new Button { Text = "TEST", Location = new Point(120, 155), Size = new Size(100, 30), BackColor = Color.FromArgb(0, 122, 204), FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 7, FontStyle.Bold), ForeColor = Color.White };
            btnTestListener.Click += (s, e) => TestListenerConnection();

            grpListenerControl.Controls.Add(lblListenerKey); grpListenerControl.Controls.Add(txtListenerApiKey);
            grpListenerControl.Controls.Add(lblListenerModel); grpListenerControl.Controls.Add(txtListenerModel);
            grpListenerControl.Controls.Add(btnSaveListener);
            grpListenerControl.Controls.Add(btnTestListener);

            // Set Initial Selection
            if (cmbMicListenerProvider.Items.Contains(currentListenerProvider)) cmbMicListenerProvider.SelectedItem = currentListenerProvider;
            else if (cmbMicListenerProvider.Items.Count > 0) cmbMicListenerProvider.SelectedIndex = 0;
            LoadListenerIntoUI();

            // === TTS PROVIDER PANEL ===
            GroupBox grpTTSControl = CreateGroup(LanguageManager.GetUITTSControl(), 830, 1385, 230, 200); // Shifted down (was 1345)
            grpTTSControl.ForeColor = Color.HotPink;
            this.Controls.Add(grpTTSControl);

            Label lblTTS = new Label { Text = LanguageManager.CurrentLanguage == LanguageManager.AppLanguage.Spanish ? "PROVEEDOR DE VOZ (TTS):" : "VOICE PROVIDER (TTS):", Location = new Point(10, 20), ForeColor = Color.LightGray, Font = new Font("Segoe UI", 7, FontStyle.Bold), AutoSize = true };
            cmbTTSProvider = new ComboBox { 
                Location = new Point(10, 35), 
                Size = new Size(210, 28), 
                BackColor = Color.FromArgb(30,30,30), 
                ForeColor = Color.HotPink, 
                FlatStyle = FlatStyle.Flat, 
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 10, FontStyle.Bold)
            };
            cmbTTSProvider.Items.Add("Local");
            cmbTTSProvider.Items.Add("HuggingFace");
            cmbTTSProvider.Items.Add("Piper");
            cmbTTSProvider.SelectedIndexChanged += (s, e) => LoadTTSIntoUI();
            grpTTSControl.Controls.Add(lblTTS);
            grpTTSControl.Controls.Add(cmbTTSProvider);

            Label lblTTSKey = CreateLabel("API Key / Secreto (HF):", 7, FontStyle.Regular, Color.Gray, new Point(10, 70));
            txtTTSApiKey = new TextBox { Location = new Point(10, 85), Size = new Size(210, 20), BackColor = Color.FromArgb(40,40,40), ForeColor = Color.Gold, PasswordChar = '*', Font = new Font("Consolas", 8), BorderStyle = BorderStyle.FixedSingle };
            
            Label lblTTSModel = CreateLabel(LanguageManager.CurrentLanguage == LanguageManager.AppLanguage.Spanish ? "Modelo:" : "Model:", 7, FontStyle.Regular, Color.Gray, new Point(10, 110));
            txtTTSModel = new TextBox { Location = new Point(10, 125), Size = new Size(210, 20), BackColor = Color.FromArgb(40,40,40), ForeColor = Color.Cyan, Font = new Font("Consolas", 8), BorderStyle = BorderStyle.FixedSingle };
            
            Button btnSaveTTS = new Button { Text = LanguageManager.GetBtnSave(), Location = new Point(10, 155), Size = new Size(100, 30), BackColor = Color.FromArgb(45, 45, 48), FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 7, FontStyle.Bold), ForeColor = Color.White };
            btnSaveTTS.Click += (s, e) => SaveTTSSettings();

            Button btnTestTTS = new Button { Text = "TEST", Location = new Point(120, 155), Size = new Size(100, 30), BackColor = Color.FromArgb(0, 122, 204), FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 7, FontStyle.Bold), ForeColor = Color.White };
            btnTestTTS.Click += (s, e) => {
                string provName = ttsProvider.CurrentProvider.ToString();
                ttsProvider.Speak("Hola, probando el sistema de voz " + provName);
            };

            grpTTSControl.Controls.Add(lblTTSKey); grpTTSControl.Controls.Add(txtTTSApiKey);
            grpTTSControl.Controls.Add(lblTTSModel); grpTTSControl.Controls.Add(txtTTSModel);
            grpTTSControl.Controls.Add(btnSaveTTS);
            grpTTSControl.Controls.Add(btnTestTTS);

            LoadTTSIntoUI();

            // === COMMAND LIST ===
            pnlCommandList = CreateScrollableGroup(LanguageManager.GetUICommandList(), 830, 1595, 230, 300); // Shifted down (was 1555)
            this.Controls.Add(pnlCommandList);
            PopulateCommandListUI();

            // === DATA SNAPSHOT ===
            GroupBox grpSnapFinal = CreateGroup(LanguageManager.GetUISnapshot(), 830, 1905, 230, 160); // Shifted down (was 1865)
            this.Controls.Add(grpSnapFinal);
            txtSnapshotPreview = CreateTextBox(8);
            txtSnapshotPreview.ReadOnly = true;
            grpSnapFinal.Controls.Add(txtSnapshotPreview);

            // Set a larger enough size for the form to enable scrolling
            this.AutoScrollMinSize = new Size(1100, 1800);

            // === QUIZ SECTION ===
            GroupBox grpQuiz = CreateGroup(LanguageManager.GetUIQuiz(), 20, 1130, 790, 480);
            this.Controls.Add(grpQuiz);

            pnlQuizRoot = new Panel { 
                Location = new Point(10, 20), 
                Size = new Size(770, 380), 
                AutoScroll = true,
                BackColor = Color.FromArgb(30,30,30),
                BorderStyle = BorderStyle.None
            };
            grpQuiz.Controls.Add(pnlQuizRoot);

            Button btnAddQuestion = new Button { 
                Text = LanguageManager.CurrentLanguage == LanguageManager.AppLanguage.Spanish ? "+ AÑADIR PREGUNTA" : "+ ADD QUESTION", 
                Location = new Point(10, 405), 
                Size = new Size(380, 30), 
                BackColor = Color.FromArgb(45, 45, 48), 
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat 
            };
            btnAddQuestion.Click += (s, e) => {
                SyncQuizEditsToMemory();
                quizItems.Add(new QuizItem { Question = LanguageManager.CurrentLanguage == LanguageManager.AppLanguage.Spanish ? "Nueva Pregunta" : "New Question", Answers = new List<string> { LanguageManager.CurrentLanguage == LanguageManager.AppLanguage.Spanish ? "Respuesta" : "Answer" } });
                PopulateQuizUI();
            };
            grpQuiz.Controls.Add(btnAddQuestion);

            Button btnSaveQuiz = new Button { 
                Text = LanguageManager.GetBtnSave() + " QUIZ (JSON)", 
                Location = new Point(400, 405), 
                Size = new Size(380, 30), 
                BackColor = Color.FromArgb(0, 122, 204), 
                FlatStyle = FlatStyle.Flat, 
                Font = new Font("Segoe UI", 9, FontStyle.Bold) 
            };
            btnSaveQuiz.Click += (s, e) => SaveQuiz();
            grpQuiz.Controls.Add(btnSaveQuiz);

            LoadQuiz();
            PopulateQuizUI();

            // Set a larger enough size for the form to enable scrolling
            this.AutoScrollMinSize = new Size(1100, 2000);
        }

        private void SetGameMode(GameMode mode)
        {
            currentMode = mode;
            btnModeDefault.BackColor = (mode == GameMode.Default) ? Color.FromArgb(0, 122, 204) : Color.FromArgb(45, 45, 48);
            btnModeChat.BackColor = (mode == GameMode.Chat) ? Color.FromArgb(0, 122, 204) : Color.FromArgb(45, 45, 48);
            btnModeQuiz.BackColor = (mode == GameMode.Quiz) ? Color.FromArgb(0, 122, 204) : Color.FromArgb(45, 45, 48);
            
            if (mode == GameMode.Chat) {
                AppendLog("--- MODO CHAT ACTIVADO ---");
            } else if (mode == GameMode.Quiz) {
                AppendLog("--- MODO QUIZ ACTIVADO ---");
                StartQuiz();
            } else {
                AppendLog("--- MODO MISION ACTIVADO ---");
            }

            // Sync Profile UI visibility
            // Profiles are visible in BOTH Chat and Mission modes now, but with different content
            bool showProfiles = (mode == GameMode.Chat || mode == GameMode.Default);
            
            if (cmbProfiles != null) cmbProfiles.Visible = showProfiles;
            if (btnSaveProfile != null) btnSaveProfile.Visible = showProfiles;
            if (btnDeleteProfile != null) btnDeleteProfile.Visible = showProfiles;
            if (chkIsDefault != null) chkIsDefault.Visible = showProfiles;
            if (lblProf != null) 
            {
                lblProf.Visible = showProfiles;
                lblProf.Text = (LanguageManager.CurrentLanguage == LanguageManager.AppLanguage.Spanish) 
                    ? ((mode == GameMode.Chat) ? "PERSONALIDADES:" : "PLANTILLAS MISIÓN:")
                    : ((mode == GameMode.Chat) ? "PERSONALITIES:" : "MISSION TEMPLATES:");
            }
            if (txtFullPrompt != null) txtFullPrompt.Height = showProfiles ? 180 : 220;

            if (showProfiles) RefreshProfilesList();
            UpdatePreview(true);
        }

        private void PopulateCommandListUI()
        {
            if (pnlCommandList == null) return;
            pnlCommandList.Controls.Clear();
            phraseInputs.Clear();
            
            Label lblTitle = new Label { Text = "EDITOR DE COMANDOS VOZ / CHAT", Font = new Font("Segoe UI", 7, FontStyle.Bold), ForeColor = Color.Gold, Location = new Point(5, 5), AutoSize = true };
            pnlCommandList.Controls.Add(lblTitle);

            var commands = IntentClassifier.GetCommands();
            int y = 25;
            phraseInputs.Clear();
            descriptionInputs.Clear();

            foreach (var cmd in commands)
            {
                Label lblTitleCmd = new Label { 
                    Text = "CMD: " + cmd.Command.ToUpper(), 
                    Location = new Point(5, y), 
                    Font = new Font("Segoe UI", 7, FontStyle.Bold), 
                    ForeColor = Color.Gold, 
                    Width = 200,
                    Height = 14  // Explicit height
                };
                
                Label lblPh  = new Label { 
                    Text = "Frases activadoras (separar por coma):", 
                    Location = new Point(5, y + 18),  // Increased from y+15 to y+18
                    Font = new Font("Segoe UI", 6), 
                    ForeColor = Color.Gray, 
                    Width = 200,
                    Height = 12  // Explicit height
                };
                
                TextBox txtPhrases = new TextBox { 
                    Multiline = true,
                    Text = string.Join(", ", cmd.Phrases), 
                    Location = new Point(5, y + 35),  // Increased from y+30 to y+35
                    Size = new Size(200, 50),  // Increased height from 45 to 50
                    BackColor = Color.FromArgb(30, 30, 30), 
                    ForeColor = Color.Cyan, 
                    Font = new Font("Consolas", 8),
                    BorderStyle = BorderStyle.FixedSingle,
                    Padding = new Padding(3),  // Add internal padding
                    Tag = cmd
                };

                Label lblResp = new Label { 
                    Text = "Respuesta de Tails:", 
                    Location = new Point(5, y + 90),  // Increased from y+75 to y+90
                    Font = new Font("Segoe UI", 6), 
                    ForeColor = Color.Gray, 
                    Width = 200,
                    Height = 12  // Explicit height
                };
                
                TextBox txtDesc = new TextBox { 
                    Text = cmd.Description, 
                    Location = new Point(5, y + 107),  // Increased from y+90 to y+107
                    Size = new Size(200, 26),  // Increased height from 24 to 26
                    BackColor = Color.FromArgb(30, 30, 30), 
                    ForeColor = Color.Yellow, 
                    Font = new Font("Segoe UI", 8),
                    BorderStyle = BorderStyle.FixedSingle,
                    Padding = new Padding(3)  // Add internal padding
                };

                pnlCommandList.Controls.Add(lblTitleCmd);
                pnlCommandList.Controls.Add(lblPh);
                pnlCommandList.Controls.Add(txtPhrases);
                pnlCommandList.Controls.Add(lblResp);
                pnlCommandList.Controls.Add(txtDesc);

                phraseInputs.Add(txtPhrases);
                descriptionInputs.Add(txtDesc);
                y += 145;  // Increased from 125 to 145 for better spacing
            }

            Button btnSaveCommands = new Button { 
                Text = "GUARDAR COMANDOS", 
                Location = new Point(5, y + 5), 
                Size = new Size(200, 30), 
                BackColor = Color.ForestGreen, 
                FlatStyle = FlatStyle.Flat, 
                Font = new Font("Segoe UI", 8, FontStyle.Bold) 
            };
            btnSaveCommands.Click += (s, e) => SaveCommandsFromUI();
            pnlCommandList.Controls.Add(btnSaveCommands);
        }

        private void SaveCommandsFromUI()
        {
            List<IntentClassifier.VoiceCommand> newCommands = new List<IntentClassifier.VoiceCommand>();
            for (int i = 0; i < phraseInputs.Count; i++)
            {
                var txtP = phraseInputs[i];
                var txtD = descriptionInputs[i];
                var original = (IntentClassifier.VoiceCommand)txtP.Tag;

                var phrases = txtP.Text.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                      .Select(p => p.Trim())
                                      .ToList();
                
                newCommands.Add(new IntentClassifier.VoiceCommand {
                    Phrases = phrases,
                    Command = original.Command,
                    Description = txtD.Text.Trim(),
                    Sound = original.Sound
                });
            }
            IntentClassifier.SaveCommands(newCommands);
            AppendLog("COMANDOS GUARDADOS Y APLICADOS.");
            MessageBox.Show("Comandos guardados correctamente.");
        }

        private void SendManualChat(string message)
        {
            if (string.IsNullOrEmpty(message)) return;

            // STABILITY v20: Ensure this runs on a background thread
            if (!System.Threading.Thread.CurrentThread.IsThreadPoolThread)
            {
                System.Threading.ThreadPool.QueueUserWorkItem((_) => SendManualChat(message));
                return;
            }

            // --- UNIFIED CLASSIFIER (JSON + LLM) ---
            var intent = IntentClassifier.Classify(message);
            if (intent.IsTool)
            {
                 AppendLog("INTENT: " + intent.Command + " (SFX: " + intent.Sound + ")");
                 ExecuteIntent(intent);
                 return;
            }

            if (currentMode == GameMode.Quiz && currentQuizIndex != -1)
            {
                AppendLog("USER (QUIZ): " + message);
                SendToGameHUD("USER:" + message); 
                
                // Give the user a moment (1.2s) to see their own text on the HUD before Tails replies
                System.Threading.Thread.Sleep(1200);
                
                CheckQuizAnswer(message);
                return;
            }

            // LOGICA DE CHAT ORIGINAL
            AppendLog("USER: " + message);
            SendToGameHUD("USER:" + message); 
            
            string template = txtFullPrompt.Text;
            Func<string, string> s = (k) => telemetryData.ContainsKey(k) ? telemetryData[k] : "Desconocido";
            string snapshot = GetSnapshot(s);

            // Assembly: Replace [[SNAPSHOT]] and set user message
            string finalPrompt = template.Replace("[[SNAPSHOT]]", snapshot)
                                         .Replace("[[NIVEL]]", txtLevelInfo.Text)
                                         .Replace("Información del Nivel", txtLevelInfo.Text)
                                         .Replace("[[CONTEXTO]]", txtGeneralContext.Text)
                                         .Replace("Responde con prioridad en lo que te preguntan", txtGeneralContext.Text);

            if (!finalPrompt.Contains("[MENSAJE USUARIO]")) {
                finalPrompt = finalPrompt.Replace("<|im_start|>assistant", "\nMENSAJE DEL USUARIO: " + message + "\n<|im_start|>assistant");
            } else {
                finalPrompt = finalPrompt.Replace("[MENSAJE USUARIO]", message);
            }

            SendToIA(LanguageManager.EnforcePromptLanguage(finalPrompt));
        }

        private void ExecuteIntent(IntentClassifier.IntentResult intent)
        {
            if (intent == null) return;
            
            // 1. EJECUTAR COMANDO EN EL JUEGO
            if (!string.IsNullOrEmpty(intent.Command))
            {
                AppendLog("AI_EXEC: " + intent.Command);
                SendCommandToGame(intent.Command);
            }

            // 2. REPRODUCIR SONIDO (SI HAY)
            if (!string.IsNullOrEmpty(intent.Sound)) 
            {
                SendCommandToGame("playsound " + intent.Sound);
            }

            // 3. RESPUESTA VISUAL Y VOZ
            if (!string.IsNullOrEmpty(intent.ResponseText))
            {
                SendToGameHUD("TAILS : " + intent.ResponseText);
                
                // Audio Sync Delay (non-blocking)
                System.Threading.ThreadPool.QueueUserWorkItem((_) => {
                    System.Threading.Thread.Sleep(200); 
                    this.Invoke(new Action(() => SendToCoquiTTS(intent.ResponseText)));
                });
            }
        }

        private void LoadLevelLore()
        {
            try
            {
                string path = @"mod\levels_info.json";
                if (File.Exists(path))
                {
                    string content = File.ReadAllText(path).Trim('{', '}', '\r', '\n', ' ');
                    string[] entries = content.Split(new[] { "\",", "}," }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var entry in entries)
                    {
                        string[] kv = entry.Split(new[] { "\": \"" }, StringSplitOptions.RemoveEmptyEntries);
                        if (kv.Length == 2)
                        {
                            string key = kv[0].Replace("\"", "").Trim();
                            string val = kv[1].Replace("\"", "").Trim();
                            levelLore[key] = val;
                        }
                    }
                }
            }
            catch { }
        }

        private string GetStrategyFocus(string type)
        {
            if (type.Contains("VITALES")) return "Comenta sobre la salud/rings de Sonic.";
            if (type.Contains("NAVEGACI\u00d3N")) return "Comenta sobre el progreso o velocidad.";
            if (type.Contains("PELIGROS")) return "Alerta sobre amenazas o entorno.";
            if (type.Contains("MIXTO")) return "Observación combinando datos.";
            if (type.Contains("HUMOR")) return "Chiste corto o comentario ácido sobre la telemetría.";
            return "Observación general.";
        }

        private void AdvanceStrategy(bool isManual = false, int delta = 1)
        {
            // Stop iteration completely if not in Default mode
            if (currentMode != GameMode.Default) return;

            // Si estï¿½ pausado y NO es manual (vï¿½a timer), sï¿½lo actualizamos el preview 
            if (isStrategyPaused && !isManual) 
            {
                UpdatePreview();
                return; 
            }

            currentStrategyIndex += delta;
            if (currentStrategyIndex >= strategySlots.Count) currentStrategyIndex = 0;
            if (currentStrategyIndex < 0) currentStrategyIndex = strategySlots.Count - 1;
            
            HighlightActiveStrategy();

            // === AUTO-SWITCH PROFILE BASED ON TYPE ===
            string type = (strategySlots != null && currentStrategyIndex < strategySlots.Count) ? strategySlots[currentStrategyIndex].Text : "VITALES";
            string searchType = type.Split(' ')[0]; // Toma "VITALES" de "VITALES (Status)"
            
            var matchingProfile = missionProfiles.FirstOrDefault(p => p.Name.ToUpper().Contains(searchType.ToUpper()));
            
            // Si no existe un perfil para este tipo, lo creamos para que el usuario pueda editarlo por separado
            if (matchingProfile == null)
            {
                matchingProfile = new PromptProfile { Name = searchType, Template = txtFullPrompt.Text, IsDefault = false };
                missionProfiles.Add(matchingProfile);
                RefreshProfilesList(searchType);
            }

            if (matchingProfile != null && cmbProfiles != null)
            {
                isProgrammaticUpdate = true;
                cmbProfiles.SelectedItem = matchingProfile.Name;
                txtFullPrompt.Text = matchingProfile.Template;
                isProgrammaticUpdate = false;
                AppendLog("PERFIL MISI\u00d3N: " + matchingProfile.Name);
            }

            UpdatePreview();
            
            if (!type.Contains("SILENCIO"))
            {
                string strategyFocus = GetStrategyFocus(type);
                string finalPrompt = BuildFinalPrompt(txtFullPrompt.Text, strategyFocus);
                SendToIA(finalPrompt);
            }
        }

        private void HighlightActiveStrategy()
        {
            for(int i=0; i < strategyPanels.Count; i++)
            {
                if (i == currentStrategyIndex)
                    strategyPanels[i].BackColor = Color.FromArgb(0, 122, 204);
                else
                    strategyPanels[i].BackColor = Color.FromArgb(35, 35, 35);
            }
        }

        private void RequestTelemetry()
        {
            try
            {
                using (var client = new System.Net.Sockets.TcpClient("127.0.0.1", 1235))
                using (var stream = client.GetStream())
                {
                    byte[] data = System.Text.Encoding.ASCII.GetBytes("TELEMETRY\n");
                    stream.Write(data, 0, data.Length);

                    byte[] buffer = new byte[1024];
                    int bytes = stream.Read(buffer, 0, buffer.Length);
                    string response = System.Text.Encoding.ASCII.GetString(buffer, 0, bytes);
                    ParseContent(response);
                }
            }
            catch { }
        }

        private void ParseContent(string content)
        {
            if (string.IsNullOrEmpty(content)) return;
            
            string[] parts = content.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i += 2)
            {
                if (i + 1 < parts.Length)
                {
                    string key = parts[i];
                    string val = parts[i + 1];
                    
                    if (key == "STATE") 
                    {
                        string stateText = val;
                        switch(val)
                        {
                            case "0": stateText = "NULL"; break;
                            case "1": stateText = "EN NIVEL"; break;
                            case "2": stateText = "INTERMISION"; break;
                            case "3": stateText = "ESPERANDO"; break;
                            case "4": stateText = "CINEMATICA"; break;
                            case "9": stateText = "MENU"; break;
                            case "10": stateText = "TITULO"; break;
                            default: stateText = "Estado " + val; break;
                        }
                        UpdateStat("ESTADO", stateText);
                    }
                    else if (key == "RINGS") UpdateStat("RINGS", val);
                    else if (key == "LIVES") UpdateStat("VIDAS", val);
                    else if (key == "SPEED") UpdateStat("VELOCIDAD", val);
                    else if (key == "SCORE") UpdateStat("SCORE", val);
                    else if (key == "TIME") 
                    {
                        int tics = 0;
                        if (int.TryParse(val, out tics))
                        {
                            int seconds = tics / 35;
                            UpdateStat("TIEMPO", seconds + "s");
                        }
                        else UpdateStat("TIEMPO", val);
                    }
                    else if (key == "NIVEL") 
                    {
                        UpdateStat("NIVEL", "MAP" + val.PadLeft(2, '0'));
                        if (levelLore.ContainsKey(val))
                        {
                            string lore = levelLore[val];
                            if (txtLevelInfo.InvokeRequired) txtLevelInfo.Invoke(new Action(() => txtLevelInfo.Text = lore));
                            else txtLevelInfo.Text = lore;
                            telemetryData["INFO_ENTORNO"] = lore;
                        }
                        else 
                        {
                            if (txtLevelInfo.InvokeRequired) txtLevelInfo.Invoke(new Action(() => txtLevelInfo.Text = "No hay datos de lore para este mapa."));
                            else txtLevelInfo.Text = "No hay datos de lore para este mapa.";
                            telemetryData["INFO_ENTORNO"] = "Entorno desconocido.";
                        }
                    }
                    else if (key == "MAPNAME") UpdateStat("NOMBRE DE MAPA", val.Replace("_", " "));
                    else if (key == "ENEMY_TYPE") UpdateStat("TIPO DE ENEMIGOS", val.Replace("_", " "));
                    else if (key == "O_TYPE") 
                    {
                        string rawType = val.Replace("_", " ");
                        // Semantic mapping for AI clarity
                        if (rawType.StartsWith("Checkpoint "))
                            telemetryData["O_TYPE"] = rawType.Replace("Checkpoint ", "Checkpoint n├║mero ");
                        else if (rawType == "Final del Nivel")
                            telemetryData["O_TYPE"] = "Final del nivel";
                        else if (rawType == "Token Special Stage")
                            telemetryData["O_TYPE"] = "Token de etapa especial";
                        else if (rawType == "Emblema")
                            telemetryData["O_TYPE"] = "Emblema";
                        else if (rawType == "Esmeralda del Caos")
                            telemetryData["O_TYPE"] = "Esmeralda del caos";
                        else
                            telemetryData["O_TYPE"] = rawType;
                    }
                    else if (key == "O_DIST") 
                    {
                        int dist = 0;
                        if (int.TryParse(val, out dist))
                        {
                            string oType = telemetryData.ContainsKey("O_TYPE") ? telemetryData["O_TYPE"] : "Objetivo";
                            if (dist < 0) UpdateStat("OBJETIVO", "No detectado");
                            else {
                                int meters = dist / 64;
                                UpdateStat("OBJETIVO", oType + " a " + meters + " metros");
                            }
                        }
                        else UpdateStat("OBJETIVO", "Desconocido");
                    }
                    else if (key == "E_DIST") 
                    {
                        // No longer updating label here, handled by ENEMY_TYPE name lookup
                        telemetryData["E_DIST_RAW"] = val; 
                    }
                    else if (key == "HINT") 
                    {
                        int hints = 0;
                        if (int.TryParse(val, out hints))
                        {
                            telemetryData["HINT_TOUCHWATER"] = ((hints & 2) != 0) ? "1" : "0";
                            telemetryData["HINT_UNDERWATER"] = ((hints & 16) != 0) ? "1" : "0";
                            telemetryData["HINT_DROWNING"] = ((hints & 4) != 0) ? "1" : "0";
                            telemetryData["HINT_BLOCKED"] = ((hints & 1) != 0) ? "1" : "0";
                            telemetryData["HINT_FLYING"] = ((hints & 32) != 0) ? "1" : "0";
                        }
                        else
                        {
                            telemetryData["HINT_TOUCHWATER"] = "0";
                            telemetryData["HINT_UNDERWATER"] = "0";
                            telemetryData["HINT_DROWNING"] = "0";
                            telemetryData["HINT_BLOCKED"] = "0";
                            telemetryData["HINT_FLYING"] = "0";
                        }
                    }
                    else if (key == "WATER") 
                    {
                        telemetryData["WATER"] = val;
                    }
                    else if (key == "CHECKPOINT")
                    {
                        int checkpoint = 0;
                        if (int.TryParse(val, out checkpoint))
                        {
                            if (checkpoint == 0) UpdateStat("CHECKPOINT", "Ninguno");
                            else UpdateStat("CHECKPOINT", "Checkpoint #" + checkpoint);
                        }
                        else UpdateStat("CHECKPOINT", "--");
                    }
                    else if (key == "TIMESHIT")
                    {
                        int timeshit = 0;
                        if (int.TryParse(val, out timeshit))
                        {
                            if (timeshit == 0) UpdateStat("GOLPEADO", "Intacto");
                            else if (timeshit == 1) UpdateStat("GOLPEADO", "1 golpe");
                            else UpdateStat("GOLPEADO", timeshit + " golpes");
                        }
                        else UpdateStat("GOLPEADO", "--");
                    }
                    
                    telemetryData[key] = val;
                }
            }

            // Post-parsing derived states (Avoid race conditions with HINT bits)
            int drown = 0;
            if (telemetryData.ContainsKey("DROWN")) int.TryParse(telemetryData["DROWN"], out drown);
            
            bool touchWater = telemetryData.ContainsKey("HINT_TOUCHWATER") && telemetryData["HINT_TOUCHWATER"] == "1";
            bool underwater = telemetryData.ContainsKey("HINT_UNDERWATER") && telemetryData["HINT_UNDERWATER"] == "1";
            bool drowning = telemetryData.ContainsKey("HINT_DROWNING") && telemetryData["HINT_DROWNING"] == "1";
            bool blocked = telemetryData.ContainsKey("HINT_BLOCKED") && telemetryData["HINT_BLOCKED"] == "1";
            bool flying = telemetryData.ContainsKey("HINT_FLYING") && telemetryData["HINT_FLYING"] == "1";
            
            if (flying)
                UpdateStat("AMBIENTE", "Volando con Tails");
            else if (underwater)
                UpdateStat("AMBIENTE", "Bajo el agua");
            else if (touchWater)
                UpdateStat("AMBIENTE", "Pies en el agua");
            else
                UpdateStat("AMBIENTE", "En tierra");
            
            if (drowning && drown > 0)
            {
                int seconds = drown / 35;
                UpdateStat("PELIGRO DE AHOGARSE", "S├ì - Aire: " + seconds + "s");
            }
            else
            {
                UpdateStat("PELIGRO DE AHOGARSE", "NO");
            }
            
            if (blocked)
                UpdateStat("BLOQUEADO", "S├ì - Chocando contra pared");
            else
                UpdateStat("BLOQUEADO", "NO");

            UpdatePreview();
        }

        private void UpdateStat(string key, string value)
        {
            if (statsLabels.ContainsKey(key))
            {
                if (statsLabels[key].InvokeRequired)
                    statsLabels[key].Invoke(new Action(() => statsLabels[key].Text = value.Trim()));
                else
                    statsLabels[key].Text = value.Trim();
                telemetryData[key] = value.Trim();
            }
        }
        
        private void AppendLog(string text)
        {
            try {
                if (txtLog == null || txtLog.IsDisposed) return;
                if (txtLog.InvokeRequired) { 
                    txtLog.BeginInvoke(new Action(() => AppendLog(text))); 
                    return; 
                }
                txtLog.AppendText("[" + DateTime.Now.ToLongTimeString() + "] " + text + "\r\n");
                txtLog.SelectionStart = txtLog.Text.Length;
                txtLog.ScrollToCaret();
            } catch { }
        }

         private void SendToGameHUD(string message)
         {
             string normalized = NormalizeForGame(message);
             System.Threading.ThreadPool.QueueUserWorkItem((_) => {
                 try {
                     using (var client = new System.Net.Sockets.TcpClient("127.0.0.1", 1235))
                     using (var stream = client.GetStream()) {
                         byte[] data = System.Text.Encoding.UTF8.GetBytes("SAY_IA " + normalized + "\n");
                         stream.Write(data, 0, data.Length);
                     }
                 } catch {
                     // Game might be closed or port busy, ignore to not spam dashboard logs
                 }
             });
         }

         private string NormalizeForGame(string text)
         {
             if (string.IsNullOrEmpty(text)) return text;
             return text.Replace("├í", "a").Replace("├®", "e").Replace("├¡", "i").Replace("├│", "o").Replace("├║", "u")
                        .Replace("├ü", "A").Replace("├ë", "E").Replace("├ì", "I").Replace("├ô", "O").Replace("├Ü", "U")
                        .Replace("├▒", "n").Replace("├æ", "N")
                        .Replace("┬┐", "").Replace("┬í", "");
         }

          private void SendToCoquiTTS(string text)
          {
              if (!ttsEnabled || string.IsNullOrWhiteSpace(text)) return;
              SyncTTSLanguage();
              ttsProvider.Speak(text);
          }

          private void SyncTTSLanguage()
          {
              string langCode = LanguageManager.GetWhisperCode();
              if (ttsProvider.Language != langCode)
              {
                  ttsProvider.Language = langCode;
                  
                  // Auto-switch Piper voice if using defaults
                  if (ttsProvider.CurrentProvider == TTSProvider.ProviderType.Piper)
                  {
                      if (langCode == "en" && (string.IsNullOrEmpty(ttsProvider.Model) || ttsProvider.Model.Contains("es_AR")))
                      {
                          ttsProvider.Model = "en_US-amy-medium.onnx";
                          ttsProvider.PiperVoicePath = Path.Combine(@"mod\piper_voices", ttsProvider.Model);
                          if (txtTTSModel != null) txtTTSModel.Text = ttsProvider.Model;
                      }
                      else if (langCode == "es" && (string.IsNullOrEmpty(ttsProvider.Model) || ttsProvider.Model.Contains("en_US")))
                      {
                          ttsProvider.Model = "es_AR-daniela-high.onnx";
                          ttsProvider.PiperVoicePath = Path.Combine(@"mod\piper_voices", ttsProvider.Model);
                          if (txtTTSModel != null) txtTTSModel.Text = ttsProvider.Model;
                      }
                  }
              }
          }
         
          private string GetSnapshot(Func<string, string> s)
          {
              string environment = s("AMBIENTE");
              if (environment == "Pies en el agua") environment = "Pies en el agua o fango";
              
              string airStatus = s("PELIGRO DE AHOGARSE");
              if (airStatus == "NO") {
                  if (environment == "Volando con Tails") airStatus = "A salvo (volando con Tails)";
                  else airStatus = "Fuera de peligro (no hay riesgo de ahogo)";
              }
              else airStatus = "┬íPELIGRO! Se queda sin aire: " + airStatus;

              string movement = s("VELOCIDAD") + " metros por segundo";

              string threats = s("TIPO DE ENEMIGOS");
              if (threats == "?" || threats == "Ninguno") threats = "Ninguna amenaza detectada";
              else threats = "Enemigo cercano: " + threats;

              return string.Format(
                  "ESTADO ACTUAL:\n" +
                  "- Situaci├│n: {0} en {1} ({2}).\n" +
                  "- Salud: {3} anillos (rings), {4} vidas. Estado de da├▒o: {5}.\n" +
                  "- Entorno: {6}. Bloqueado contra pared: {7}.\n" +
                  "- Aire: {8}.\n" +
                  "- Amenazas Cercanas: {9}.\n" +
                  "- Movimiento: {10}.\n" +
                  "- Meta: {11}.",
                  s("ESTADO"), s("NOMBRE DE MAPA"), s("NIVEL"), 
                  s("RINGS"), s("VIDAS"), s("GOLPEADO"),
                  environment, s("BLOQUEADO"),
                  airStatus,
                  threats,
                  movement,
                  s("OBJETIVO")
              );
          }

        private void UpdatePreview(bool forceReset = false)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => UpdatePreview(forceReset)));
                return;
            }

            Func<string, string> s = (k) => telemetryData.ContainsKey(k) ? telemetryData[k] : "Desconocido";
            string snapshot = GetSnapshot(s);
            string levelLoreContext = txtLevelInfo != null ? txtLevelInfo.Text : "No lore data.";
            string generalContext = txtGeneralContext != null ? txtGeneralContext.Text : "Responde con prioridad en lo que te preguntan";

            // Always update the LIVE snapshot box
            if (txtSnapshotPreview != null) txtSnapshotPreview.Text = snapshot;

            if (currentMode == GameMode.Chat)
            {
                // Solo cargar si estï¿½ vacï¿½o o es un reset forzado. 
                if (forceReset || string.IsNullOrEmpty(txtFullPrompt.Text))
                {
                    LoadSelectedProfile();
                }

                if (txtPromptPreview != null) {
                    string preview = BuildFinalPrompt(txtFullPrompt.Text, "Conversaciï¿½n libre");
                    txtPromptPreview.Text = "--- MODO CHAT [LIVE UPDATE] ---\n" + preview;
                }
                return;
            }

            if (currentMode == GameMode.Quiz)
            {
                if (txtPromptPreview != null) txtPromptPreview.Text = "--- MODO QUIZ ACTIVO ---";
                return;
            }

            // --- MISION MODE (Default) ---
            string activeType = "VITALES (Status)";
            if (strategySlots != null && currentStrategyIndex < strategySlots.Count) activeType = strategySlots[currentStrategyIndex].Text;
            
            if (activeType.Contains("SILENCIO"))
            {
                if (txtPromptPreview != null) txtPromptPreview.Text = "--- SILENCIO ---";
                return;
            }

            string strategyFocus = GetStrategyFocus(activeType);
            
            if (txtPromptPreview != null) {
                // Impacto en VIVO: Usar el texto de txtFullPrompt en lugar de plantillas hardcoded
                string preview = BuildFinalPrompt(txtFullPrompt.Text, strategyFocus);
                txtPromptPreview.Text = "--- MODO MISI\u00d3N [" + activeType + "] [LIVE UPDATE] ---\n" + preview;
            }
            // DO NOT OVERWRITE txtFullPrompt.Text HERE! 
        }

        private string BuildFinalPrompt(string template, string strategyText)
        {
            Func<string, string> s = (k) => telemetryData.ContainsKey(k) ? telemetryData[k] : "Desconocido";
            string snapshot = GetSnapshot(s);
            string levelLoreContext = txtLevelInfo != null ? txtLevelInfo.Text : "No lore data.";
            string generalContext = txtGeneralContext != null ? txtGeneralContext.Text : "Responde con prioridad en lo que te preguntan";

            string result = template.Replace("[[SNAPSHOT]]", snapshot)
                           .Replace("[[NIVEL]]", levelLoreContext)
                           .Replace("Información del Nivel", levelLoreContext)
                           .Replace("[[CONTEXTO]]", generalContext)
                           .Replace("Responde con prioridad en lo que te preguntan", generalContext)
                           .Replace("[[ESTRATEGIA]]", strategyText);

            // Auto-replace any telemetry key found as [[KEY]]
            foreach (var key in telemetryData.Keys)
            {
                result = result.Replace("[[" + key + "]]", telemetryData[key]);
            }
            
            return LanguageManager.EnforcePromptLanguage(result);
        }

          private void SendToIA(string prompt)
          {
              if (string.IsNullOrEmpty(prompt) || prompt == "--- SILENCIO ---") return;

              string selectedProvider = "LM Studio";
              if (cmbAIProvider != null) {
                  this.Invoke(new Action(() => {
                      selectedProvider = cmbAIProvider.SelectedItem != null ? cmbAIProvider.SelectedItem.ToString() : "LM Studio";
                  }));
              }

              AppendLog("...");

              System.Threading.ThreadPool.QueueUserWorkItem((_) => {
                  try {
                      string provider = selectedProvider;
                      string baseUrl = "http://127.0.0.1:1234";
                      string apiKey = "";
                      string model = "";

                      if (providerConfigs.ContainsKey(provider)) {
                          var cfg = providerConfigs[provider];
                          baseUrl = cfg.ContainsKey("base_url") ? cfg["base_url"] : "http://127.0.0.1:1234";
                          apiKey = cfg.ContainsKey("api_key") ? cfg["api_key"] : "";
                          model = cfg.ContainsKey("model") ? cfg["model"] : "";
                      }

                      bool useChatAPI = (provider == "DeepSeek" || provider == "ChatGPT" || provider == "OpenRouter");
                      string finalBase = (baseUrl ?? "").TrimEnd('/');
                      if (useChatAPI && !finalBase.EndsWith("/v1")) finalBase += "/v1";
                      string url = useChatAPI ? finalBase + "/chat/completions" : finalBase + "/v1/completions";

                      var request = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
                      request.Method = "POST";
                      request.ContentType = "application/json";
                      request.Accept = "application/json";
                      request.Timeout = 60000;

                      if (url.ToLower().StartsWith("https")) {
                          System.Net.ServicePointManager.SecurityProtocol = (System.Net.SecurityProtocolType)12288 | (System.Net.SecurityProtocolType)3072;
                          request.Headers.Add("HTTP-Referer", "http://localhost:1234");
                          request.Headers.Add("X-Title", "SRB2 Telemetry");
                      }

                      if (!string.IsNullOrEmpty(apiKey)) {
                          request.Headers.Add("Authorization", "Bearer " + apiKey.Trim());
                      }

                      string json = BuildProviderJson(prompt, useChatAPI, model);
                      byte[] bytes = System.Text.Encoding.UTF8.GetBytes(json);
                      request.ContentLength = bytes.Length;

                      using (var stream = request.GetRequestStream()) {
                          stream.Write(bytes, 0, bytes.Length);
                      }

                      using (var response = (System.Net.HttpWebResponse)request.GetResponse())
                      using (var reader = new System.IO.StreamReader(response.GetResponseStream())) {
                          string result = reader.ReadToEnd();
                          string aiText = ExtractAIText(result, useChatAPI);
                          if (!string.IsNullOrWhiteSpace(aiText)) {
                              aiText = CleanAIResponse(aiText);
                              if (!string.IsNullOrWhiteSpace(aiText)) {
                                  AppendLog(aiText);
                                  SendToGameHUD("TAILS : " + aiText);
                                  SendToCoquiTTS(aiText);
                              } else AppendLog("...");
                          } else AppendLog("--- ERROR PARSING ---");
                      }
                  } catch (System.Net.WebException webEx) {
                      string errorMsg = webEx.Message;
                      if (webEx.Response != null) {
                          using (var errorReader = new System.IO.StreamReader(webEx.Response.GetResponseStream())) {
                              errorMsg += " -> " + errorReader.ReadToEnd();
                          }
                      }
                      AppendLog("--- ERROR RED ---");
                  } catch (Exception ex) {
                      AppendLog("--- ERROR INTERNO ---");
                  }
              });
          }

        private Label CreateLabel(string txt, float size, FontStyle style, Color color, Point loc)
        {
            return new Label { Text = txt, Font = new Font("Segoe UI", size, style), ForeColor = color, Location = loc, AutoSize = true };
        }

        private GroupBox CreateGroup(string title, int x, int y, int w, int h)
        {
            return new GroupBox { Text = title, Location = new Point(x, y), Size = new Size(w, h), ForeColor = Color.LightGray };
        }

        private Panel CreateScrollableGroup(string title, int x, int y, int w, int h)
        {
            Panel panel = new Panel();
            panel.Location = new Point(x, y);
            panel.Size = new Size(w, h);
            panel.AutoScroll = true;
            panel.BorderStyle = BorderStyle.FixedSingle;
            panel.BackColor = Color.FromArgb(30, 30, 30);
            
            Label lblTitle = new Label();
            lblTitle.Text = title;
            lblTitle.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            lblTitle.ForeColor = Color.LightGray;
            lblTitle.Location = new Point(5, 5);
            lblTitle.AutoSize = true;
            panel.Controls.Add(lblTitle);
            
            return panel;
        }

        private void CreateStatCard(Control parent, string title, int x, int y, int w, Color? valColor = null)
        {
            int offsetY = parent is Panel ? 25 : 0;
            
            Label lblTitle = new Label { Text = title, Location = new Point(x, y + offsetY), Font = new Font("Segoe UI", 8), ForeColor = Color.Gray, AutoSize = true };
            Label lblVal = new Label { Text = "--", Location = new Point(x, y + offsetY + 15), Font = new Font("Segoe UI", 12, FontStyle.Bold), ForeColor = valColor ?? Color.White, AutoSize = true };
            
            parent.Controls.Add(lblTitle);
            parent.Controls.Add(lblVal);
            statsLabels[title] = lblVal;
        }

        private TextBox CreateTextBox(float size)
        {
            return new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BackColor = Color.FromArgb(45, 45, 48), ForeColor = Color.White, Font = new Font("Consolas", size), BorderStyle = BorderStyle.None };
        }

        private void LoadProfiles()
        {
            LoadProfileList(chatProfilesPath, chatProfiles, true);
            LoadProfileList(missionProfilesPath, missionProfiles, false);
            RefreshProfilesList();
        }

        private void LoadProfileList(string path, List<PromptProfile> list, bool isChat)
        {
            list.Clear();
            if (File.Exists(path))
            {
                try {
                    string content = File.ReadAllText(path);
                    string[] items = content.Split(new string[] { "}," }, StringSplitOptions.None);
                    foreach (var item in items)
                    {
                        string n = ExtractJsonValue(item, "Name");
                        string t = ExtractJsonValue(item, "Template");
                        string d = ExtractJsonValue(item, "IsDefault");
                        bool isDef = (d == "true" || d == "True");
                        if (!string.IsNullOrEmpty(n)) list.Add(new PromptProfile { Name = n, Template = t, IsDefault = isDef });
                    }
                } catch { }
            }
            if (isChat && list.Count == 0)
            {
                list.Add(new PromptProfile { 
                    Name = "Tails Standard", 
                    Template = "<|im_start|>system: \nEres Tails.\nREGLAS: [NUNCA uses etiquetas <think> ni razones, M\u00c1XIMO 2 ORACIONES CORTAS. PROHIBIDO acciones entre asteriscos, sin s\u00edmbolos ni explicaciones.]\nPersonalidad: \u00c1cida, burlona, c\u00ednica y divertida, leal. Se\u00f1alas lo obvio con humor. Hablas siempre en ESPA\u00d1OL.\nNivel: [[NIVEL]]\nContexto: [[CONTEXTO]]\n<|im_end|>\n<|im_start|>user\n[[SNAPSHOT]]\n\nMENSAJE: [MENSAJE USUARIO]\n<|im_end|>\n<|im_start|>assistant",
                    IsDefault = true
                });
            }
            else if (!isChat)
            {
                // Ensure specific mission types exist
                string[] standardTypes = { "VITALES", "NAVEGACION", "PELIGROS", "MIXTO", "HUMOR" };
                foreach (var st in standardTypes)
                {
                    if (!list.Any(p => p.Name.ToUpper().Contains(st)))
                    {
                        list.Add(new PromptProfile { 
                            Name = st, 
                            Template = "<|im_start|>system\nEres Tails (" + st + "). [[REGLAS: MAX 2 ORACS]].\nContexto: [[CONTEXTO]]\n<|im_end|>\n<|im_start|>user\n[[SNAPSHOT]]\n<|im_end|>\n<|im_start|>assistant", 
                            IsDefault = (st == "VITALES") 
                        });
                    }
                }
            }
        }

        private string ExtractJsonValue(string json, string key)
        {
            try {
                string search = "\"" + key + "\"";
                int keyIdx = json.IndexOf(search);
                if (keyIdx == -1) return "";
                
                int colonIdx = json.IndexOf(":", keyIdx);
                if (colonIdx == -1) return "";
                
                // Buscar el inicio del valor ignorando espacios
                int valStart = colonIdx + 1;
                while (valStart < json.Length && (json[valStart] == ' ' || json[valStart] == '\r' || json[valStart] == '\n' || json[valStart] == '\t')) valStart++;
                
                if (valStart >= json.Length) return "";

                if (json[valStart] == '\"')
                {
                    int qStart = valStart;
                    int qEnd = json.IndexOf("\"", qStart + 1);
                    while (qEnd != -1 && json[qEnd - 1] == '\\') {
                        qEnd = json.IndexOf("\"", qEnd + 1);
                    }
                    if (qEnd == -1) return "";
                    return json.Substring(qStart + 1, qEnd - qStart - 1).Replace("\\n", "\n").Replace("\\\"", "\"");
                }
                else
                {
                    // Valor no entrecomillado (ej: booleanos true/false)
                    int endIdx = json.IndexOfAny(new char[] { ',', '}', ']' }, valStart);
                    if (endIdx == -1) endIdx = json.Length;
                    return json.Substring(valStart, endIdx - valStart).Trim().ToLower();
                }
            } catch { return ""; }
        }

        private void LoadSelectedProfile()
        {
            var list = ActiveProfiles;
            if (cmbProfiles.SelectedIndex >= 0 && cmbProfiles.SelectedIndex < list.Count)
            {
                var p = list[cmbProfiles.SelectedIndex];
                txtFullPrompt.Text = p.Template;
                isProgrammaticUpdate = true;
                chkIsDefault.Checked = p.IsDefault;
                isProgrammaticUpdate = false;
            }
        }

        private void ToggleDefaultProfile(bool isDefault)
        {
            if (isProgrammaticUpdate) return;
            var list = ActiveProfiles;
            
            if (cmbProfiles.SelectedIndex >= 0 && cmbProfiles.SelectedIndex < list.Count)
            {
                var current = list[cmbProfiles.SelectedIndex];
                
                if (isDefault)
                {
                    // Unset others
                    foreach (var p in list) p.IsDefault = false;
                    current.IsDefault = true;
                }
                else
                {
                    current.IsDefault = false;
                }
                
                SaveProfilesToDisk();
            }
        }

        private void SaveCurrentProfile()
        {
            using (Form prompt = new Form())
            {
                prompt.Width = 300; prompt.Height = 180; prompt.Text = "Guardar Perfil";
                prompt.StartPosition = FormStartPosition.CenterParent;
                Label textLabel = new Label() { Left = 20, Top = 20, Text = "Nombre del Perfil:", Width = 200 };
                TextBox textBox = new TextBox() { Left = 20, Top = 45, Width = 240 };
                CheckBox chkDefault = new CheckBox() { Left = 20, Top = 75, Text = "Establecer como Default", Width = 200 };
                Button confirmation = new Button() { Text = "Guardar", Left = 160, Width = 100, Top = 105, DialogResult = DialogResult.OK };
                prompt.Controls.Add(textBox); prompt.Controls.Add(chkDefault); prompt.Controls.Add(confirmation); prompt.Controls.Add(textLabel);
                prompt.AcceptButton = confirmation;

                if (prompt.ShowDialog() == DialogResult.OK && !string.IsNullOrWhiteSpace(textBox.Text))
                {
                    string name = textBox.Text.Trim();
                    bool isDef = chkDefault.Checked;
                    var list = ActiveProfiles;

                    if (isDef)
                    {
                        foreach (var p in list) p.IsDefault = false;
                    }

                    var existing = list.Find(p => p.Name == name);
                    if (existing != null) 
                    {
                        existing.Template = txtFullPrompt.Text;
                        if (isDef) existing.IsDefault = true;
                    }
                    else 
                    {
                        list.Add(new PromptProfile { Name = name, Template = txtFullPrompt.Text, IsDefault = isDef });
                    }
                    SaveProfilesToDisk();
                    RefreshProfilesList(name);
                }
            }
        }

        private void DeleteSelectedProfile()
        {
            var list = ActiveProfiles;
            if (cmbProfiles.SelectedIndex >= 0 && list.Count > 1)
            {
                list.RemoveAt(cmbProfiles.SelectedIndex);
                SaveProfilesToDisk();
                RefreshProfilesList();
            }
        }

        private void RefreshProfilesList(string selectName = null)
        {
            cmbProfiles.Items.Clear();
            var list = ActiveProfiles;
            foreach (var p in list) cmbProfiles.Items.Add(p.Name);
            
            if (selectName != null) 
            {
                cmbProfiles.SelectedItem = selectName;
            }
            else 
            {
                // Auto-select default
                var defProfile = list.FirstOrDefault(p => p.IsDefault);
                if (defProfile != null) cmbProfiles.SelectedItem = defProfile.Name;
                else if (cmbProfiles.Items.Count > 0) cmbProfiles.SelectedIndex = 0;
            }
        }

        private void SaveProfilesToDisk()
        {
            var list = ActiveProfiles;
            string path = ActiveProfilePath;
            
            StringBuilder sb = new StringBuilder();
            sb.Append("[\n");
            for (int i = 0; i < list.Count; i++)
            {
                sb.Append("  {\n");
                sb.Append("    \"Name\": \"").Append(list[i].Name).Append("\",\n");
                sb.Append("    \"Template\": \"").Append(list[i].Template.Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "")).Append("\",\n");
                sb.Append("    \"IsDefault\": \"").Append(list[i].IsDefault ? "true" : "false").Append("\"\n");
                sb.Append("  }");
                if (i < list.Count - 1) sb.Append(",\n");
            }
            sb.Append("\n]");
            File.WriteAllText(path, sb.ToString());
        }

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Dashboard());
        }

        // === GLOBAL KEYBOARD HOOK ===

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        private IntPtr SetHook(LowLevelKeyboardProc proc)
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                return SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int vkCode = Marshal.ReadInt32(lParam);
                if (vkCode == VK_C) // Tecla C
                {
                    // Ensure we are in a mode that allows voice (allowing Default, Chat, and Quiz)
                    if (currentMode == GameMode.Chat || currentMode == GameMode.Default || currentMode == GameMode.Quiz)
                    {
                    if (wParam == (IntPtr)WM_KEYDOWN)
                    {
                        if (!isListening && !isProcessing)
                        {
                            isListening = true;
                            StartListening();
                        }
                    }
                    else if (wParam == (IntPtr)WM_KEYUP)
                    {
                        if (isListening)
                        {
                            isListening = false;
                            isProcessing = true; // Lock
                            System.Threading.ThreadPool.QueueUserWorkItem((_) => StopListening());
                        }
                    }
                    }
                }
            }
            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        private void StartListening()
        {
            try {
                AppendLog("ESCUCHANDO...");
                peakVolume = 0f;
                
                audioStream = new MemoryStream();
                
                // Start NAudio Recording
                waveIn = new WaveInEvent();
                waveIn.DeviceNumber = cmbMics.SelectedIndex >= 0 ? cmbMics.SelectedIndex : 0;
                waveIn.WaveFormat = new WaveFormat(16000, 1); // 16kHz Mono
                waveIn.DataAvailable += (s, e) => {
                    if (waveWriter != null) {
                        waveWriter.Write(e.Buffer, 0, e.BytesRecorded);
                        
                        // Volume calculation (16-bit PCM)
                        float currentPeak = 0f;
                        for (int i = 0; i < e.BytesRecorded; i += 2)
                        {
                            short sample = (short)((e.Buffer[i + 1] << 8) | e.Buffer[i]);
                            float sample32 = Math.Abs(sample / 32768f);
                            if (sample32 > currentPeak) currentPeak = sample32;
                        }
                        if (currentPeak > peakVolume) peakVolume = currentPeak;
                        
                        // Update UI meter (Invoke if needed)
                        this.BeginInvoke(new Action(() => {
                            pbVolume.Value = (int)Math.Min(100, currentPeak * 200); // Scale up for visibility
                        }));
                    }
                };
                
                waveWriter = new WaveFileWriter(audioStream, waveIn.WaveFormat);
                waveIn.StartRecording();
                
                SendCommandToGame("ai_listening 1");
            } catch (Exception ex) {
                AppendLog("ERROR AUDIO: " + ex.Message);
            }
        }

        private void StopListening()
        {
            try {
                AppendLog("PROCESANDO...");
                
                SendCommandToGame("ai_listening 0");
                this.BeginInvoke(new Action(() => pbVolume.Value = 0));
                
                // Stop NAudio Recording
                if (waveIn != null) {
                    waveIn.StopRecording();
                    waveIn.Dispose();
                    waveIn = null;
                }
                
                byte[] audioData = null;
                if (waveWriter != null) {
                    waveWriter.Dispose(); // Finalizes the WAV header in the stream
                    waveWriter = null;
                    audioData = audioStream.ToArray();
                    audioStream.Dispose();
                    audioStream = null;
                }
                
                if (audioData != null && audioData.Length > 0) {
                    AppendLog("AUDIO: " + audioData.Length + " bytes | PICO: " + (peakVolume * 100).ToString("0.0") + "%");
                    
                    // v20: Full background execution flow to keep UI responsive
                    System.Threading.ThreadPool.QueueUserWorkItem((_) => {
                        try {
                            string transcription = SendToVoiceEngine(audioData);
                            if (!string.IsNullOrEmpty(transcription) && !transcription.StartsWith("ERROR"))
                            {
                                SendManualChat(transcription);
                            }
                            else if (string.IsNullOrEmpty(transcription))
                            {
                                AppendLog("AVISO: Transcripción vacía (posible silencio o ruido).");
                            }
                            else
                            {
                                AppendLog(transcription); // Logs "ERROR: ..."
                            }
                        } finally {
                            isProcessing = false; // Unlock
                        }
                    });
                } else {
                    AppendLog("ERROR: No se capturó audio.");
                    isProcessing = false; // Unlock
                }
            } catch (Exception ex) {
                AppendLog("ERROR: " + ex.Message);
            }
        }

        // Groq Transcription API
        private string SendToVoiceEngineGroq(byte[] audioBytes)
        {
            try
            {
                // Enable TLS 1.2 for Groq API (required for secure HTTPS connections)
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
                
                if (audioBytes == null || audioBytes.Length == 0) {
                    return "ERROR: No audio data";
                }

                string apiKey = listenerConfigs.ContainsKey("Groq") ? listenerConfigs["Groq"]["api_key"] : "";
                string model = listenerConfigs.ContainsKey("Groq") ? listenerConfigs["Groq"]["model"] : "whisper-large-v3-turbo";
                
                if (string.IsNullOrEmpty(apiKey)) {
                    return "ERROR: Groq API Key no configurada";
                }

                string url = "https://api.groq.com/openai/v1/audio/transcriptions";
                var request = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
                request.Method = "POST";
                request.Timeout = 30000;
                
                string boundary = "----WebKitFormBoundary" + DateTime.Now.Ticks.ToString("x");
                request.ContentType = "multipart/form-data; boundary=" + boundary;
                request.Headers.Add("Authorization", "Bearer " + apiKey);
                
                using (var requestStream = request.GetRequestStream())
                {
                    byte[] boundaryBytes = Encoding.UTF8.GetBytes("--" + boundary + "\r\n");
                    requestStream.Write(boundaryBytes, 0, boundaryBytes.Length);
                    
                    string header = "Content-Disposition: form-data; name=\"file\"; filename=\"audio.wav\"\r\nContent-Type: audio/wav\r\n\r\n";
                    byte[] headerBytes = Encoding.UTF8.GetBytes(header);
                    requestStream.Write(headerBytes, 0, headerBytes.Length);
                    
                    requestStream.Write(audioBytes, 0, audioBytes.Length);
                    
                    // Add model field
                    byte[] modelBoundaryBytes = Encoding.UTF8.GetBytes("\r\n--" + boundary + "\r\n");
                    requestStream.Write(modelBoundaryBytes, 0, modelBoundaryBytes.Length);
                    
                    string modelHeader = "Content-Disposition: form-data; name=\"model\"\r\n\r\n";
                    byte[] modelHeaderBytes = Encoding.UTF8.GetBytes(modelHeader);
                    requestStream.Write(modelHeaderBytes, 0, modelHeaderBytes.Length);
                    
                    byte[] modelBytes = Encoding.UTF8.GetBytes(model);
                    requestStream.Write(modelBytes, 0, modelBytes.Length);
                    
                    byte[] trailerBytes = Encoding.UTF8.GetBytes("\r\n--" + boundary + "--\r\n");
                    
                    // Add language field
                    byte[] langBoundaryBytes = Encoding.UTF8.GetBytes("\r\n--" + boundary + "\r\n");
                    requestStream.Write(langBoundaryBytes, 0, langBoundaryBytes.Length);
                    string langHeader = "Content-Disposition: form-data; name=\"language\"\r\n\r\n";
                    byte[] langHeaderBytes = Encoding.UTF8.GetBytes(langHeader);
                    requestStream.Write(langHeaderBytes, 0, langHeaderBytes.Length);
                    byte[] langBytes = Encoding.UTF8.GetBytes(LanguageManager.GetWhisperCode());
                    requestStream.Write(langBytes, 0, langBytes.Length);
                    
                    requestStream.Write(trailerBytes, 0, trailerBytes.Length);
                }
                
                using (var response = (System.Net.HttpWebResponse)request.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream()))
                {
                    string jsonResponse = reader.ReadToEnd();
                    AppendLog("DEBUG JSON (Groq): " + (jsonResponse.Length > 50 ? jsonResponse.Substring(0, 50) + "..." : jsonResponse));
                    
                    // Parse JSON response: { "text": "transcription" }
                    int textStart = jsonResponse.IndexOf("\"text\"");
                    if (textStart >= 0)
                    {
                        int colonPos = jsonResponse.IndexOf(":", textStart);
                        int quoteStart = jsonResponse.IndexOf("\"", colonPos + 1);
                        int quoteEnd = jsonResponse.IndexOf("\"", quoteStart + 1);
                        
                        while (quoteEnd != -1 && jsonResponse[quoteEnd - 1] == '\\') {
                            quoteEnd = jsonResponse.IndexOf("\"", quoteEnd + 1);
                        }

                        if (quoteStart >= 0 && quoteEnd > quoteStart)
                        {
                            string result = jsonResponse.Substring(quoteStart + 1, quoteEnd - quoteStart - 1);
                            result = result.Replace("\\\"", "\"").Trim();
                            result = System.Text.RegularExpressions.Regex.Unescape(result);
                            return result;
                        }
                    }
                    return jsonResponse;
                }
            }
            catch (Exception ex)
            {
                return "ERROR: " + ex.Message;
            }
        }

        private string SendToVoiceEngine(byte[] audioBytes)
        {
            // Check which provider is selected
            if (currentListenerProvider == "Groq")
            {
                AppendLog("USANDO GROQ PARA TRANSCRIPCIÓN...");
                return SendToVoiceEngineGroq(audioBytes);
            }
            
            // Default: Whisper Local
            try
            {
                if (audioBytes == null || audioBytes.Length == 0) {
                    return "ERROR: No audio data";
                }

        // Send audio bytes via HTTP POST
        string url = "http://localhost:18888/api/transcribe";
        var request = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
        request.Method = "POST";
        request.Timeout = 15000;
        
        string boundary = "----WebKitFormBoundary" + DateTime.Now.Ticks.ToString("x");
        request.ContentType = "multipart/form-data; boundary=" + boundary;
        
        using (var requestStream = request.GetRequestStream())
        {
            // Note: Boundary for the first part SHOULD NOT have leading \r\n according to RFC but many servers are lenient.
            // Curl doesn't send it.
            byte[] boundaryBytes = Encoding.UTF8.GetBytes("--" + boundary + "\r\n");
            requestStream.Write(boundaryBytes, 0, boundaryBytes.Length);
            
            string header = "Content-Disposition: form-data; name=\"file\"; filename=\"audio.wav\"\r\nContent-Type: audio/wav\r\n\r\n";
            byte[] headerBytes = Encoding.UTF8.GetBytes(header);
            requestStream.Write(headerBytes, 0, headerBytes.Length);
            
            requestStream.Write(audioBytes, 0, audioBytes.Length);
            
            byte[] trailerBytes = Encoding.UTF8.GetBytes("\r\n--" + boundary + "--\r\n");
            requestStream.Write(trailerBytes, 0, trailerBytes.Length);
        }
        
        using (var response = (System.Net.HttpWebResponse)request.GetResponse())
        using (var reader = new StreamReader(response.GetResponseStream()))
        {
            string jsonResponse = reader.ReadToEnd();
            AppendLog("DEBUG JSON (Voice): " + (jsonResponse.Length > 50 ? jsonResponse.Substring(0, 50) + "..." : jsonResponse));
            // AppendLog("DEBUG JSON: " + jsonResponse); // Descomentar para ver respuesta completa
            
            // AI Command: Clean up hallucinations common in Whisper
            if (jsonResponse.Contains("Gracias por ver") || jsonResponse.Contains("Subscríbete") || jsonResponse.Contains("Thanks for watching") || jsonResponse.Contains("Adiós") || jsonResponse.Contains("be right back") || jsonResponse.Contains("Thank you"))
            {
                // AppendLog("DEBUG: Hallucination detected and filtered.");
                return "";
            }

            // Parse JSON response: { "text": "transcription" }
            int textStart = jsonResponse.IndexOf("\"text\"");
            if (textStart >= 0)
            {
                int colonPos = jsonResponse.IndexOf(":", textStart);
                int quoteStart = jsonResponse.IndexOf("\"", colonPos + 1);
                int quoteEnd = jsonResponse.IndexOf("\"", quoteStart + 1);
                
                // Handle escaped quotes in transcription
                while (quoteEnd != -1 && jsonResponse[quoteEnd - 1] == '\\') {
                    quoteEnd = jsonResponse.IndexOf("\"", quoteEnd + 1);
                }

                if (quoteStart >= 0 && quoteEnd > quoteStart)
                {
                    string result = jsonResponse.Substring(quoteStart + 1, quoteEnd - quoteStart - 1);
                    result = result.Replace("\\\"", "\"").Trim();
                    
                    // Simple regex-based Unicode unescaper for \uXXXX
                    result = System.Text.RegularExpressions.Regex.Unescape(result);
                    
                    return result;
                }
            }
            return jsonResponse;
        }
            }
            catch (Exception ex)
            {
                return "ERROR: " + ex.Message;
            }
        }

        private void SendCommandToGame(string cmd)
        {
            if (string.IsNullOrEmpty(cmd)) return;
            AppendLog("CMD -> GAME: " + cmd);
            
            try {
                using (TcpClient client = new TcpClient("127.0.0.1", 1235))
                using (NetworkStream stream = client.GetStream()) {
                    byte[] data = Encoding.UTF8.GetBytes(cmd + "\n");
                    stream.Write(data, 0, data.Length);
                }
            } catch (Exception ex) {
                AppendLog("TCP ERR: " + ex.Message);
            }
        }

        private int currentQuizIndex = -1;
        private void StartQuiz(int forcedIndex = -1)
        {
            if (quizItems.Count == 0) LoadQuiz();
            if (quizItems.Count > 0)
            {
                currentQuizIndex = (forcedIndex != -1 && forcedIndex < quizItems.Count) ? forcedIndex : new Random().Next(quizItems.Count);
                var q = quizItems[currentQuizIndex].Question;
                SendToGameHUD("TAILS : " + LanguageManager.GetQuizStartHUD() + q);
                SendToCoquiTTS(LanguageManager.GetQuizStartSpeech() + q);
            }
        }

        private void CheckQuizAnswer(string userResponse)
        {
            if (currentQuizIndex == -1 || currentQuizIndex >= quizItems.Count) return;
            
            var item = quizItems[currentQuizIndex];
            string cleanResponse = userResponse.ToLower().Trim().Replace(".", "").Replace("!", "").Replace("¿", "").Replace("?", "");
            bool correct = false;
            foreach (var alt in item.Answers)
            {
                if (cleanResponse.Contains(alt.ToLower().Trim()))
                {
                    correct = true;
                    break;
                }
            }

            if (correct)
            {
                string bestAns = item.Answers[0];
                if (LanguageManager.CurrentLanguage == LanguageManager.AppLanguage.English && item.Answers.Count > 1) bestAns = item.Answers[1];

                string resp = LanguageManager.GetQuizCorrect(bestAns);
                SendToGameHUD("TAILS : " + resp);
                SendToCoquiTTS(resp);
                
                // Reward Logic based on selected reward
                string reward = item.Reward ?? "Rings";
                if (reward == "Gargola" || reward == "Gargoyle") {
                    SendCommandToGame("ai_spawn_gargoyle");
                } else if (reward == "Moneda de Mario" || reward == "Mario Coin") {
                    SendCommandToGame("ai_spawn_coin");
                } else if (reward == "Caja de vida extra" || reward == "Extra Life Box") {
                    SendCommandToGame("ai_spawn_1up");
                } else if (reward == "Caja de anillos" || reward == "Rings Box") {
                    SendCommandToGame("ai_spawn_ringbox");
                } else {
                    // Default: Thrown Rings (for realism)
                    SendCommandToGame("ai_spawn_ring");
                }

                SendCommandToGame("playsound itemup");
                AppendLog("QUIZ: CORRECTO (" + userResponse + ") - Recompensa: " + reward);
            }
            else
            {
                string bestAns = item.Answers[0];
                if (LanguageManager.CurrentLanguage == LanguageManager.AppLanguage.English && item.Answers.Count > 1) bestAns = item.Answers[1];

                string resp = LanguageManager.GetQuizIncorrect(bestAns);
                SendToGameHUD("TAILS : " + resp);
                SendToCoquiTTS(resp);
                SendCommandToGame("playsound shldls"); // Sonido de daño/escudo perdido
                SendCommandToGame("ai_hurt"); // Penalización: Sonic golpeado
                AppendLog("QUIZ: INCORRECTO (" + userResponse + ") - Sonic golpeado");
            }

            // Next question after a delay
            System.Threading.ThreadPool.QueueUserWorkItem((_) => {
                System.Threading.Thread.Sleep(4000);
                this.Invoke(new Action(() => StartQuiz()));
            });
        }

        private void LoadQuiz()
        {
            quizItems.Clear();
            if (File.Exists(quizPath))
            {
                try {
                    string json = File.ReadAllText(quizPath);
                    string[] items = json.Split(new[] { "}," }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var item in items)
                    {
                        string q = ExtractJsonValue(item, "question");
                        string rew = ExtractJsonValue(item, "reward");
                        if (string.IsNullOrEmpty(rew)) rew = "Rings";

                        string aRaw = "";
                        int start = item.IndexOf("\"answers\":");
                        if (start != -1) {
                            int end = item.IndexOf("]", start);
                            if (end != -1) aRaw = item.Substring(start, end - start + 1);
                        }

                        List<string> answers = new List<string>();
                        if (!string.IsNullOrEmpty(aRaw)) {
                            var matches = Regex.Matches(aRaw, @"""([^""]+)""");
                            foreach (Match m in matches) {
                                string val = m.Groups[1].Value;
                                if (val == "answers") continue; // Surgical fix: skip the key name
                                answers.Add(val);
                            }
                        }
                        if (!string.IsNullOrEmpty(q)) quizItems.Add(new QuizItem { Question = q, Answers = answers, Reward = rew });
                    }
                } catch { }
            }
            if (quizItems.Count == 0) {
                quizItems.Add(new QuizItem { Question = "¿Qué animal es Sonic?", Answers = new List<string> { "erizo", "hedgehog" }, Reward = "Rings" });
            }
        }

        private void PopulateQuizUI()
        {
            pnlQuizRoot.Controls.Clear();
            quizQuestionsUI.Clear();
            quizAnswersUI.Clear();
            quizRewardsUI.Clear();
            int y = 5;
            for (int i=0; i < quizItems.Count; i++)
            {
                var item = quizItems[i];
                Label lblQ = new Label { Text = (LanguageManager.CurrentLanguage == LanguageManager.AppLanguage.Spanish ? "PREGUNTA " : "QUESTION ") + (i+1), Location = new Point(5, y), ForeColor = Color.Gold, Font = new Font("Segoe UI", 8, FontStyle.Bold), AutoSize = true };
                TextBox txtQ = new TextBox { 
                    Text = item.Question, 
                    Multiline = true,
                    Location = new Point(5, y + 20), 
                    Size = new Size(680, 35), 
                    BackColor = Color.FromArgb(45, 45, 48), 
                    ForeColor = Color.White, 
                    Font = new Font("Segoe UI", 11),
                    BorderStyle = BorderStyle.FixedSingle,
                    Padding = new Padding(5)
                };
                
                Label lblA = new Label { Text = LanguageManager.CurrentLanguage == LanguageManager.AppLanguage.Spanish ? "RESPUESTAS (Variantes para voz/chat):" : "ANSWERS (Variations for voice/chat):", Location = new Point(5, y + 60), ForeColor = Color.Gray, Font = new Font("Segoe UI", 7), AutoSize = true };
                TextBox txtA = new TextBox { 
                    Text = string.Join(", ", item.Answers), 
                    Multiline = true,
                    Location = new Point(5, y + 75), 
                    Size = new Size(680, 35), 
                    BackColor = Color.FromArgb(45, 45, 48), 
                    ForeColor = Color.Cyan, 
                    Font = new Font("Segoe UI", 11),
                    BorderStyle = BorderStyle.FixedSingle,
                    Padding = new Padding(5)
                };

                Label lblRew = new Label { 
                    Text = LanguageManager.CurrentLanguage == LanguageManager.AppLanguage.Spanish ? "RECOMPENSA AL ACERTAR:" : "REWARD IF CORRECT:", 
                    Location = new Point(5, y + 115), 
                    ForeColor = Color.LightGray, 
                    Font = new Font("Segoe UI", 7, FontStyle.Bold), 
                    AutoSize = true 
                };
                ComboBox cmbRew = new ComboBox {
                    Location = new Point(5, y + 130),
                    Size = new Size(680, 25),
                    BackColor = Color.FromArgb(40, 40, 40),
                    ForeColor = Color.Gold,
                    FlatStyle = FlatStyle.Flat,
                    DropDownStyle = ComboBoxStyle.DropDownList
                };
                string[] rewardOptions = LanguageManager.CurrentLanguage == LanguageManager.AppLanguage.Spanish 
                    ? new string[] { "Rings", "Gargola", "Moneda de Mario", "Caja de vida extra", "Caja de anillos" }
                    : new string[] { "Rings", "Gargoyle", "Mario Coin", "Extra Life Box", "Rings Box" };
                
                cmbRew.Items.AddRange(rewardOptions);
                
                // Set selection based on item reward
                string currentReward = item.Reward ?? "Rings";
                int selIdx = 0;
                for (int j = 0; j < rewardOptions.Length; j++) {
                    if (rewardOptions[j].Equals(currentReward, StringComparison.OrdinalIgnoreCase)) {
                        selIdx = j;
                        break;
                    }
                }
                cmbRew.SelectedIndex = selIdx;

                Button btnPlay = new Button { Text = "▶", Location = new Point(695, y + 20), Size = new Size(40, 35), BackColor = Color.ForestGreen, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Tag = i };
                btnPlay.Click += (s, e) => {
                    int idx = (int)((Button)s).Tag;
                    StartQuiz(idx);
                };

                Button btnRem = new Button { Text = "X", Location = new Point(695, y + 75), Size = new Size(40, 35), BackColor = Color.Maroon, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Tag = i };
                btnRem.Click += (s, e) => {
                    SyncQuizEditsToMemory();
                    int idx = (int)((Button)s).Tag;
                    if (idx >= 0 && idx < quizItems.Count) {
                        quizItems.RemoveAt(idx);
                        PopulateQuizUI();
                    }
                };

                pnlQuizRoot.Controls.Add(lblQ);
                pnlQuizRoot.Controls.Add(txtQ);
                pnlQuizRoot.Controls.Add(lblA);
                pnlQuizRoot.Controls.Add(txtA);
                pnlQuizRoot.Controls.Add(lblRew);
                pnlQuizRoot.Controls.Add(cmbRew);
                pnlQuizRoot.Controls.Add(btnPlay);
                pnlQuizRoot.Controls.Add(btnRem);
                
                quizQuestionsUI.Add(txtQ);
                quizAnswersUI.Add(txtA);
                quizRewardsUI.Add(cmbRew);
                y += 180;
            }
        }

        private void SyncQuizEditsToMemory()
        {
            if (quizQuestionsUI.Count == 0) return;
            List<QuizItem> currentItems = new List<QuizItem>();
            for (int i = 0; i < quizQuestionsUI.Count; i++)
            {
                var q = quizQuestionsUI[i].Text.Trim();
                var a = quizAnswersUI[i].Text.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                           .Select(s => s.Trim())
                                           .ToList();
                var rew = (quizRewardsUI.Count > i && quizRewardsUI[i].SelectedItem != null) ? quizRewardsUI[i].SelectedItem.ToString() : "Rings";
                if (!string.IsNullOrEmpty(q)) currentItems.Add(new QuizItem { Question = q, Answers = a, Reward = rew });
            }
            quizItems = currentItems;
        }

        private void SaveQuiz()
        {
            try {
                SyncQuizEditsToMemory();
                
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("[");
                for (int i = 0; i < quizItems.Count; i++)
                {
                    sb.Append("  { \"question\": \"").Append(quizItems[i].Question.Replace("\"", "\\\"")).Append("\", ");
                    sb.Append("\"reward\": \"").Append((quizItems[i].Reward ?? "Rings").Replace("\"", "\\\"")).Append("\", ");
                    sb.Append("\"answers\": [");
                    for (int j = 0; j < quizItems[i].Answers.Count; j++)
                    {
                        sb.Append("\"").Append(quizItems[i].Answers[j].Replace("\"", "\\\"")).Append("\"").Append(j < quizItems[i].Answers.Count - 1 ? ", " : "");
                    }
                    sb.Append("] }");
                    sb.AppendLine(i < quizItems.Count - 1 ? "," : "");
                }
                sb.AppendLine("]");
                
                File.WriteAllText(quizPath, sb.ToString(), Encoding.UTF8);
                AppendLog("QUIZ GUARDADO EN: " + quizPath);
                MessageBox.Show(LanguageManager.CurrentLanguage == LanguageManager.AppLanguage.Spanish ? "Quiz guardado correctamente." : "Quiz saved successfully.");
            } catch (Exception ex) {
                AppendLog("ERR GUARDANDO QUIZ: " + ex.Message);
                MessageBox.Show("Error al guardar Quiz: " + ex.Message);
            }
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);
    }
}
