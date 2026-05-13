namespace LauncherCatalog.Models;

public sealed class LanguageOption
{
    public LanguageOption(string code, string nativeName)
    {
        Code = code;
        NativeName = nativeName;
    }

    public string Code { get; }
    public string NativeName { get; }
}
