using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ContextMenuStudio.Core;

public static partial class ConfigurationStore
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
    public static string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ContextMenuStudio");
    public static string DraftPath => Path.Combine(DataDirectory, "commands.json");
    public static string InstalledPath => Path.Combine(DataDirectory, "installed.json");
    public static string Serialize(MenuConfiguration configuration) => JsonSerializer.Serialize(configuration, JsonOptions);
    public static MenuConfiguration Parse(string json)
    {
        var config = JsonSerializer.Deserialize<MenuConfiguration>(json, JsonOptions) ?? throw new InvalidDataException("The configuration is empty.");
        Validate(config);
        return config;
    }
    public static MenuConfiguration Load(string path) => Parse(File.ReadAllText(path));
    public static void Save(string path, MenuConfiguration config)
    {
        Validate(config);
        AtomicWrite(path, Serialize(config));
    }
    public static void AtomicWrite(string path, string contents)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, contents, new UTF8Encoding(false));
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static void Validate(MenuConfiguration configuration)
    {
        if (configuration.SchemaVersion != 1) throw new InvalidDataException("Unsupported configuration version.");
        if (configuration.Commands is null || configuration.Commands.Count > 250) throw new InvalidDataException("Use at most 250 commands.");
        var ids = new HashSet<Guid>();
        foreach (var command in configuration.Commands)
        {
            if (command is null || command.Id == Guid.Empty || !ids.Add(command.Id)) throw new InvalidDataException("Every command must have a unique, non-empty ID.");
            if (string.IsNullOrWhiteSpace(command.Name) || command.Name.Length > 100 || command.Name.Any(char.IsControl)) throw new InvalidDataException("Command names must contain 1–100 characters and no control characters.");
            if (command.Group is null || command.Group.Length > 80 || command.Group.Any(char.IsControl)) throw new InvalidDataException($"Invalid group for '{command.Name}'.");
            if (command.Icon is null || command.Icon.Length > 2048 || command.Icon.Any(char.IsControl)) throw new InvalidDataException($"Invalid icon for '{command.Name}'.");
            if (!Enum.IsDefined(command.Scope) || !Enum.IsDefined(command.Engine)) throw new InvalidDataException("Unknown scope or PowerShell engine.");
            if (command.Scope == MenuScope.FileExtension && (command.Extension is null || !ExtensionPattern().IsMatch(command.Extension))) throw new InvalidDataException("Use a single extension such as .txt, .jpg, or .ps1.");
            if (string.IsNullOrWhiteSpace(command.Script) || command.Script.Length > 100_000 || command.Script.Contains('\0')) throw new InvalidDataException($"'{command.Name}' needs a script of 1–100,000 characters.");
            if (command.TimeoutSeconds is < 1 or > 3600) throw new InvalidDataException("Timeout must be between 1 and 3600 seconds.");
        }
    }
    [GeneratedRegex(@"^\.[A-Za-z0-9][A-Za-z0-9_-]{0,19}$", RegexOptions.CultureInvariant)]
    private static partial Regex ExtensionPattern();
}
