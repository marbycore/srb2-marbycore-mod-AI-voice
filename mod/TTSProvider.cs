using System;
using System.IO;
using System.Net;
using System.Text;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SRB2Dashboard
{
    public class TTSProvider
    {
        public enum ProviderType { Local, HuggingFace, Piper }
        
        public string BaseUrl { get; set; }
        public string ApiKey { get; set; }
        public string Model { get; set; }
        public string PiperPath { get; set; }
        public string PiperVoicePath { get; set; }
        public ProviderType CurrentProvider { get; set; }
        public string Language { get; set; }
        
        public event Action<string> OnLog;

        private void Log(string message)
        {
            if (OnLog != null) OnLog(message);
        }

        public TTSProvider()
        {
            BaseUrl = "http://127.0.0.1:5000";
            ApiKey = "";
            Model = "coqui/XTTS-v2";
            CurrentProvider = ProviderType.Local;
            
            // Piper Defaults
            PiperPath = @"mod\piper\piper.exe";
            PiperVoicePath = @"mod\piper_voices\es_AR-daniela-high.onnx";
            
            // Forzar TLS 1.2
            try {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | (SecurityProtocolType)768 | SecurityProtocolType.Tls;
            } catch { }

            Language = "en";
        }

        public void Speak(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            Log("TTS INVOCADO -> " + CurrentProvider);

            Task.Run(() =>
            {
                try
                {
                    if (CurrentProvider == ProviderType.HuggingFace)
                    {
                        SpeakHuggingFace(text);
                    }
                    else if (CurrentProvider == ProviderType.Piper)
                    {
                        SpeakPiper(text);
                    }
                    else
                    {
                        SpeakLocal(text);
                    }
                }
                catch (Exception ex)
                {
                    Log("TTS TASK CRASH: " + ex.Message);
                }
            });
        }

        private void SpeakLocal(string text)
        {
            try
            {
                string url = BaseUrl.TrimEnd('/') + "/api/generate";
                var request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "POST";
                request.ContentType = "application/json";
                request.Timeout = 20000;

                string cleanText = text.Replace("\"", "\\\"");
                string json = "{\"text\": \"" + cleanText + "\", \"sample\": \"tails.wav\", \"play_audio\": true, \"speed\": 1.05, \"overwrite\": true, \"prefix\": \"tails_voice\"}";
                byte[] bytes = Encoding.UTF8.GetBytes(json);

                using (var stream = request.GetRequestStream())
                {
                    stream.Write(bytes, 0, bytes.Length);
                }

                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    Log("TTS Local: OK");
                }
            }
            catch (Exception ex)
            {
                Log("TTS Local Error: " + ex.Message);
            }
        }

        private void SpeakHuggingFace(string text)
        {
            // El servidor de Hugging Face está en transición (2026). 
            // Algunos modelos requieren el Router, otros siguen en la API clásica.
            string[] endpoints = new string[] {
                "https://api-inference.huggingface.co/models/" + Model,
                "https://router.huggingface.co/hf-inference/models/" + Model,
                "https://router.huggingface.co/models/" + Model
            };

            foreach (string url in endpoints)
            {
                try
                {
                    Log("TTS HF: Intentando endpoint -> " + url);
                    if (TrySpeakHF(url, text)) return;
                }
                catch (WebException webEx)
                {
                    string body = "";
                    if (webEx.Response != null) {
                        try {
                            using (var reader = new StreamReader(webEx.Response.GetResponseStream())) {
                                body = reader.ReadToEnd();
                            }
                        } catch { }
                    }
                    
                    int statusCode = (webEx.Response != null) ? (int)((HttpWebResponse)webEx.Response).StatusCode : 0;
                    
                    // Si el error es 503 (Loading) o 429 (Rate limit), esperamos y reintentamos en el mismo endpoint
                    if (statusCode == 503 || body.Contains("loading") || body.Contains("estimated_time")) {
                        Log("Modelo cargando. Reintentando en 20s...");
                        System.Threading.Thread.Sleep(20000);
                        try { if (TrySpeakHF(url, text)) return; } catch { }
                    } 
                    // Si el error es 410 (Gone) o 404 (Not Found), saltamos al siguiente endpoint
                    else if (statusCode == 410 || statusCode == 404) {
                        Log("Endpoint obsoleto o no encontrado (Code " + statusCode + "). Probando siguiente...");
                        continue;
                    }
                    else {
                        Log("TTS HF Error (" + url + "): " + webEx.Message + " -> " + body);
                    }
                }
                catch (Exception ex)
                {
                    Log("TTS HF CRITICAL: " + ex.Message);
                }
            }
            
            Log("TTS HF: Todos los endpoints fallaron.");
        }


        private bool TrySpeakHF(string url, string text)
        {
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "POST";
            request.ContentType = "application/json";
            request.Headers.Add("Authorization", "Bearer " + ApiKey);
            request.Timeout = 120000;

            // Limpiamos el texto para el JSON
            string cleanText = text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", "");
            
            // XTTS-v2 en el Tier gratuito es más estable enviando solo inputs si no se requiere clonación específica
            // Opcionalmente se puede intentar enviar el speaker_wav si el archivo existe
            string samplePath = @"mod\audio_samples\tails.wav";
            string json = "";
            
            if (File.Exists(samplePath) && Model.Contains("XTTS"))
            {
                try {
                    byte[] bytes = File.ReadAllBytes(samplePath);
                    string base64Sample = Convert.ToBase64String(bytes);
                    json = "{\"inputs\": \"" + cleanText + "\", \"parameters\": {\"speaker_wav\": \"data:audio/wav;base64," + base64Sample + "\", \"language\": \"" + Language + "\"}}";
                } catch {
                    json = "{\"inputs\": \"" + cleanText + "\"}";
                }
            }
            else
            {
                json = "{\"inputs\": \"" + cleanText + "\"}";
            }

            byte[] bodyBytes = Encoding.UTF8.GetBytes(json);
            request.ContentLength = bodyBytes.Length;

            using (var stream = request.GetRequestStream())
            {
                stream.Write(bodyBytes, 0, bodyBytes.Length);
            }

            using (var response = (HttpWebResponse)request.GetResponse())
            {
                string contentType = response.ContentType.ToLower();
                if (contentType.Contains("audio") || contentType.Contains("octet-stream") || contentType.Contains("flac") || contentType.Contains("wav"))
                {
                    Log("TTS HF: Audio recibido (" + contentType + ").");
                    using (var ms = new MemoryStream())
                    {
                        response.GetResponseStream().CopyTo(ms);
                        PlayAudio(ms.ToArray());
                    }
                    return true;
                }
                else 
                {
                    using (var reader = new StreamReader(response.GetResponseStream()))
                    {
                        string respText = reader.ReadToEnd();
                        Log("TTS HF Unexpected Response: " + respText);
                        return false; 
                    }
                }
            }
        }


        private void SpeakPiper(string text)
        {
            try
            {
                if (!File.Exists(PiperPath))
                {
                    Log("Piper ERROR: No se encuentra piper.exe en " + PiperPath);
                    return;
                }
                if (!File.Exists(PiperVoicePath))
                {
                    Log("Piper ERROR: No se encuentra la voz en " + PiperVoicePath);
                    return;
                }

                string tempWav = Path.Combine(Path.GetTempPath(), "piper_out.wav");
                if (File.Exists(tempWav)) File.Delete(tempWav);

                Log("Piper: Generando audio local...");
                
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = PiperPath,
                    Arguments = "--model \"" + PiperVoicePath + "\" --output_file \"" + tempWav + "\"",
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    CreateNoWindow = true
                };

                using (var process = System.Diagnostics.Process.Start(startInfo))
                {
                    using (var sw = process.StandardInput)
                    {
                        if (sw.BaseStream.CanWrite)
                        {
                            sw.WriteLine(text);
                        }
                    }
                    process.WaitForExit();
                }

                if (File.Exists(tempWav))
                {
                    Log("Piper: Éxito. Reproduciendo...");
                    byte[] audioData = File.ReadAllBytes(tempWav);
                    PlayAudio(audioData);
                }
                else
                {
                    Log("Piper ERROR: No se generó el archivo de audio.");
                }
            }
            catch (Exception ex)
            {
                Log("Piper CRITICAL Error: " + ex.Message);
            }
        }

        private void PlayAudio(byte[] audioData)
        {
            try
            {
                string tempFile = Path.Combine(Path.GetTempPath(), "tails_tts_temp.wav");
                File.WriteAllBytes(tempFile, audioData);
                
                using (var player = new System.Media.SoundPlayer(tempFile))
                {
                    player.PlaySync();
                }
            }
            catch (Exception ex)
            {
                Log("TTS Play Error: " + ex.Message);
            }
        }
    }
}
