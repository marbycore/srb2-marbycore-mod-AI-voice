using System;
using System.Text;
using System.Net;
using System.IO;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace SRB2Dashboard
{
    public class IntentClassifier
    {
        public class VoiceCommand
        {
            public List<string> Phrases { get; set; }
            public string Command { get; set; }
            public string Description { get; set; }
            public string Sound { get; set; }
        }

        private static List<VoiceCommand> LoadedCommands = new List<VoiceCommand>();
        private static string CommandsPath = @"mod\voice_commands.json";

        public static List<VoiceCommand> GetCommands()
        {
            if (LoadedCommands.Count == 0 || File.GetLastWriteTime(CommandsPath) > lastLoadTime)
            {
                LoadCommands();
            }
            return LoadedCommands;
        }

        private static DateTime lastLoadTime = DateTime.MinValue;

        private static void LoadCommands()
        {
            LoadedCommands.Clear();
            if (File.Exists(CommandsPath))
            {
                try
                {
                    string json = File.ReadAllText(CommandsPath);
                    // Ultra-robust manual parsing for the specific structure
                    string[] items = json.Split(new[] { "}," }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var item in items)
                    {
                        string pRaw = ExtractJsonValue(item, "phrases");
                        string cmd = ExtractJsonValue(item, "command");
                        string desc = ExtractJsonValue(item, "description");
                        string sound = ExtractJsonValue(item, "sound");
                        
                        if (!string.IsNullOrEmpty(cmd))
                        {
                            List<string> phrases = new List<string>();
                            foreach (Match pm in Regex.Matches(pRaw, @"""([^""]+)"""))
                            {
                                phrases.Add(pm.Groups[1].Value.Trim());
                            }
                            LoadedCommands.Add(new VoiceCommand { Phrases = phrases, Command = cmd, Description = desc, Sound = sound });
                        }
                    }
                    lastLoadTime = File.GetLastWriteTime(CommandsPath);
                }
                catch (Exception ex) { Console.WriteLine("LOAD CMD ERR: " + ex.Message); }
            }
        }

        private static string ExtractJsonValue(string json, string key)
        {
            try {
                int start = json.IndexOf("\"" + key + "\"");
                if (start == -1) return "";
                int colon = json.IndexOf(":", start);
                if (colon == -1) return "";
                
                // If it's an array for phrases
                if (key == "phrases") {
                    int arrStart = json.IndexOf("[", colon);
                    int arrEnd = json.IndexOf("]", arrStart);
                    if (arrStart != -1 && arrEnd != -1) return json.Substring(arrStart, arrEnd - arrStart + 1);
                }
                
                // Standard string value
                int qStart = json.IndexOf("\"", colon);
                int qEnd = json.IndexOf("\"", qStart + 1);
                if (qStart != -1 && qEnd != -1) return json.Substring(qStart + 1, qEnd - qStart - 1);
            } catch { }
            return "";
        }

        public static void SaveCommands(List<VoiceCommand> commands)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("[");
                for (int i = 0; i < commands.Count; i++)
                {
                    var c = commands[i];
                    sb.Append("  { \"phrases\": [");
                    for (int j = 0; j < c.Phrases.Count; j++)
                    {
                        sb.Append("\"" + c.Phrases[j] + "\"" + (j < c.Phrases.Count - 1 ? ", " : ""));
                    }
                    sb.Append("], \"command\": \"" + c.Command + "\", \"description\": \"" + c.Description + "\", \"sound\": \"" + (c.Sound ?? "") + "\" }");
                    sb.AppendLine(i < commands.Count - 1 ? "," : "");
                }
                sb.AppendLine("]");
                File.WriteAllText(CommandsPath, sb.ToString());
                LoadedCommands = commands;
                lastLoadTime = DateTime.Now;
            }
            catch (Exception ex) { Console.WriteLine("SAVE CMD ERR: " + ex.Message); }
        }

        public class IntentResult
        {
            public bool IsTool { get; set; }
            public string Command { get; set; } // e.g., "addrings 10"
            public string ResponseText { get; set; } // e.g., "¡Aquí tienes 10 anillos!"
            public string Sound { get; set; }
        }

        public static IntentResult Classify(string userText)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(userText)) return new IntentResult { IsTool = false };

                string text = userText.ToLower().Trim().Replace(".", "").Replace("!", "").Replace("?", "");
                string cleanRaw = Regex.Replace(text, @"[^a-z0-9]", "");

                // --- FAST PATH (JSON Mapping) ---
                var currentCommands = GetCommands();
                foreach (var vc in currentCommands)
                {
                    foreach (var phrase in vc.Phrases)
                    {
                        string targetRaw = Regex.Replace(phrase.ToLower(), @"[^a-z0-9]", "");
                        if (!string.IsNullOrEmpty(targetRaw) && cleanRaw.Contains(targetRaw))
                        {
                            return new IntentResult { IsTool = true, Command = vc.Command, ResponseText = vc.Description, Sound = vc.Sound };
                        }
                    }
                }

                // --- LLM FALLBACK ---
                // ... rest of the code ...

                // Construct System Prompt for Tool Calling
                string systemPrompt = @"Eres Tails de Sonic the Hedgehog. Eres un zorro genio, servicial, amable y muy valiente.
Tu trabajo es decidir si el usuario quiere ejecutar una ACCIÓN o solo está conversando.

HERRAMIENTAS DISPONIBLES:
1. 'addlives <n>' -> Dar vidas. (Ej: 'dame una vida', 'vida extra')
2. 'addrings <n>' -> Dar anillos. (Ej: 'dame 10 anillos', 'necesito rings')
3. 'ai_scale <n>' -> Cambiar tamaño (1.0 = normal). (Ej: 'hazme gigante' -> 2.0, 'hazme pequeño' -> 0.5)
4. 'ai_god' -> Modo Dios / Invencible.
5. 'ai_gravity <n>' -> Gravedad (0.5 normal, 0.1 baja).
6. 'ai_force_jump' -> Forzar salto.

SI EL USUARIO PIDE UNA ACCIÓN:
Responde ÚNICAMENTE con el objeto JSON, nada más de texto.
{ ""tool"": ""comando"", ""response"": ""¡Claro! ¡Ahí tienes!"" }

SI ES SOLO CHAT:
{ ""tool"": ""chat"", ""response"": ""Ignorar"" }
";

                // Force JSON format if the model supports it (LM Studio usually does)
                string jsonPayload = "{ \"messages\": [ " +
                                     "{ \"role\": \"system\", \"content\": \"" + EscapeJson(systemPrompt) + "\" }, " +
                                     "{ \"role\": \"user\", \"content\": \"" + EscapeJson(userText) + "\" } " +
                                     "], \"temperature\": 0.0, \"max_tokens\": 150, \"stop\": [\"<|im_end|>\", \"<|im_start|>\", \"<|endoftext|>\", \"User:\", \"Assistant:\"] }";

                // Call LLM
                string response = CallLLM(jsonPayload);
                if (string.IsNullOrEmpty(response)) return new IntentResult { IsTool = false };

                // Clean <think> tags if present
                response = Regex.Replace(response, @"<think>.*?</think>", "", RegexOptions.Singleline).Trim();
                
                // If the model still has open think tags or just text around JSON, extract the BLOCK
                string json = ExtractJson(response);
                if (string.IsNullOrEmpty(json)) {
                    // Try to find ANY JSON-like structure even if think tags weren't closed
                    int start = response.IndexOf("{");
                    int end = response.LastIndexOf("}");
                    if (start != -1 && end != -1 && end > start) {
                        json = response.Substring(start, end - start + 1);
                    }
                }

                if (string.IsNullOrEmpty(json)) return new IntentResult { IsTool = false };

                // Robust Field Extraction
                string tool = ExtractField(json, "tool");
                string reply = ExtractField(json, "response");

                if (!string.IsNullOrEmpty(tool) && tool.ToLower() != "chat" && tool.ToLower() != "ignorar")
                {
                    return new IntentResult { IsTool = true, Command = tool, ResponseText = reply };
                }
            }
            catch (Exception ex)
            {
                // Log silently or return chat
                Console.WriteLine("INTENT ERROR: " + ex.Message);
            }

            return new IntentResult { IsTool = false };
        }

        private static string CallLLM(string jsonPayload)
        {
            try
            {
                string url = "http://127.0.0.1:1234/v1/chat/completions";
                var request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "POST";
                request.ContentType = "application/json";
                request.Timeout = 30000; // 30s timeout for intent distillation models

                byte[] bytes = Encoding.UTF8.GetBytes(jsonPayload);
                using (var stream = request.GetRequestStream())
                {
                    stream.Write(bytes, 0, bytes.Length);
                }

                using (var response = (HttpWebResponse)request.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream()))
                {
                    return reader.ReadToEnd();
                }
            }
            catch
            {
                return null;
            }
        }

        private static string EscapeJson(string s)
        {
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");
        }

        private static string ExtractJson(string text)
        {
            int start = text.IndexOf("{");
            int end = text.LastIndexOf("}");
            if (start != -1 && end != -1 && end > start)
            {
                return text.Substring(start, end - start + 1);
            }
            return null;
        }

        private static string ExtractField(string json, string key)
        {
            // Very naive parser, assumes simple structure
            string pattern = "\"" + key + "\"\\s*:\\s*\"([^\"]+)\"";
            var match = Regex.Match(json, pattern);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
            return null;
        }
    }
}
