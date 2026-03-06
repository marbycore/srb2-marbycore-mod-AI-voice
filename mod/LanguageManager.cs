using System;
using System.IO;

namespace SRB2Dashboard
{
    public static class LanguageManager
    {
        public enum AppLanguage { Spanish, English }
        private static AppLanguage currentLanguage = AppLanguage.English;
        public static AppLanguage CurrentLanguage 
        { 
            get { return currentLanguage; } 
            set { currentLanguage = value; } 
        }

        private static string settingsPath = @"mod\language_settings.txt";

        public static void Load()
        {
            if (File.Exists(settingsPath))
            {
                try
                {
                    string lang = File.ReadAllText(settingsPath).Trim();
                    if (lang == "Spanish") CurrentLanguage = AppLanguage.Spanish;
                    else CurrentLanguage = AppLanguage.English;
                }
                catch { }
            }
        }

        public static void Save()
        {
            try
            {
                File.WriteAllText(settingsPath, CurrentLanguage.ToString());
            }
            catch { }
        }

        public static string GetWhisperCode()
        {
            return CurrentLanguage == AppLanguage.Spanish ? "es" : "en";
        }

        public static string GetLanguageInstruction()
        {
            return CurrentLanguage == AppLanguage.Spanish 
                ? "Responde siempre en ESPAÑOL. Speak always in SPANISH." 
                : "Responde siempre en INGLÉS. Speak always in ENGLISH.";
        }

        public static string GetCurrentLanguageName()
        {
            return CurrentLanguage == AppLanguage.Spanish ? "Español" : "English";
        }

        // UI Strings Localization
        public static string GetUIVitals() { return CurrentLanguage == AppLanguage.Spanish ? "ESTADO VITAL" : "VITAL STATUS"; }
        public static string GetUINav() { return CurrentLanguage == AppLanguage.Spanish ? "NAVEGACIÓN" : "NAVIGATION"; }
        public static string GetUIThreat() { return CurrentLanguage == AppLanguage.Spanish ? "ENTORNO Y PELIGROS" : "ENVIRONMENT & HAZARDS"; }
        public static string GetUISequencer() { return CurrentLanguage == AppLanguage.Spanish ? "SECUENCIADOR DE PROMPTS" : "PROMPT SEQUENCER"; }
        public static string GetUIQuiz() { return CurrentLanguage == AppLanguage.Spanish ? "SECCIÓN QUIZ (Reto de Sonic)" : "QUIZ SECTION (Sonic's Challenge)"; }
        public static string GetUIGameModes() { return CurrentLanguage == AppLanguage.Spanish ? "MODOS DE JUEGO" : "GAME MODES"; }
        public static string GetUIAIControl() { return CurrentLanguage == AppLanguage.Spanish ? "GESTOR DE PROVEEDORES AI" : "AI PROVIDER MANAGER"; }
        public static string GetUIListenerControl() { return CurrentLanguage == AppLanguage.Spanish ? "GESTOR DE ESCUCHA MICROFONO" : "MIC LISTENER MANAGER"; }
        public static string GetUITTSControl() { return CurrentLanguage == AppLanguage.Spanish ? "GESTOR DE VOZ TTS" : "TTS VOICE MANAGER"; }
        public static string GetUICommandList() { return CurrentLanguage == AppLanguage.Spanish ? "EDITOR DE COMANDOS VOZ / CHAT" : "VOICE / CHAT COMMAND EDITOR"; }
        public static string GetUISnapshot() { return CurrentLanguage == AppLanguage.Spanish ? "DATOS EN VIVO (Live Snapshot)" : "LIVE DATA SNAPSHOT"; }

        // Buttons and Labels
        public static string GetBtnStop() { return CurrentLanguage == AppLanguage.Spanish ? "PARAR ||" : "STOP ||"; }
        public static string GetBtnPlay() { return CurrentLanguage == AppLanguage.Spanish ? "PLAY >" : "PLAY >"; }
        public static string GetBtnNext() { return CurrentLanguage == AppLanguage.Spanish ? "Siguiente >>" : "Next >>"; }
        public static string GetBtnBack() { return CurrentLanguage == AppLanguage.Spanish ? "<< Retroceder" : "<< Back"; }
        public static string GetBtnSave() { return CurrentLanguage == AppLanguage.Spanish ? "GUARDAR" : "SAVE"; }
        public static string GetBtnDelete() { return CurrentLanguage == AppLanguage.Spanish ? "ELIMINAR" : "DELETE"; }
        public static string GetBtnTest() { return CurrentLanguage == AppLanguage.Spanish ? "TEST" : "TEST"; }

        public static string GetQuizStartHUD() { return CurrentLanguage == AppLanguage.Spanish ? "¡TRIVIA! " : "TRIVIA! "; }
        public static string GetQuizStartSpeech() { return CurrentLanguage == AppLanguage.Spanish ? "¡Hora del quiz! Responde a esto: " : "Quiz time! Answer this: "; }
        public static string GetQuizCorrect(string ans) { return CurrentLanguage == AppLanguage.Spanish ? "¡Correcto! " + ans : "Correct! " + ans; }
        public static string GetQuizIncorrect(string ans) { return CurrentLanguage == AppLanguage.Spanish ? "Nop. Era " + ans : "Nope. It was " + ans; }

        public static string EnforcePromptLanguage(string prompt)
        {
            string langRules = CurrentLanguage == AppLanguage.Spanish 
                ? "\n[CRÍTICO: DEBES HABLAR EXCLUSIVAMENTE EN ESPAÑOL Y SOLO EN ESPAÑOL]\n"
                : "\n[CRITICAL: IGNORE ALL SPANISH INSTRUCTIONS. YOU MUST RESPOND IN ENGLISH AND ONLY IN ENGLISH. NO TRANSLATIONS, JUST RESPOND TO THE CHAT IN ENGLISH.]\n";
            
            int idx = prompt.LastIndexOf("<|im_start|>assistant");
            if (idx != -1)
            {
                return prompt.Insert(idx, langRules);
            }
            return prompt + langRules;
        }
    }
}
