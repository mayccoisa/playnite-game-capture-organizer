namespace GameCaptureOrganizer
{
    /// <summary>
    /// Identidade da extensao num lugar so. O <see cref="ExtensionId"/> precisa ser IGUAL ao Id do
    /// extension.yaml: e por ele que o atualizador escolhe o .pext certo quando a release tem mais
    /// de um arquivo.
    /// </summary>
    public static class PluginIdentity
    {
        public const string ExtensionId = "GameCaptureOrganizer_20189013-0d54-4b01-ac25-655e6855bfe8";
        public const string PluginGuid = "20189013-0d54-4b01-ac25-655e6855bfe8";
        public const string Repo = "mayccoisa/playnite-game-capture-organizer";
        public const string MenuSection = "Organizador de Capturas";
        public const string DisplayName = "Organizador de Capturas";
    }
}
