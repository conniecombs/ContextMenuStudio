namespace ContextMenuStudio.Core;

public static class MenuPlan
{
    public const string TargetSuffix = "|CMS";
    public const string RootName = "ContextMenuStudio";
    public const string OwnerId = "a00a0f57-554c-449e-9876-31023c39ecb3";
    public static readonly string[] BaseParents = [@"*\shell", @"Directory\shell", @"Directory\Background\shell", @"DesktopBackground\Shell", @"Drive\shell"];
    public static string Parent(MenuCommand command) => command.Scope switch
    {
        MenuScope.Files => BaseParents[0],
        MenuScope.Folders => BaseParents[1],
        MenuScope.FolderBackground => BaseParents[2],
        MenuScope.DesktopBackground => BaseParents[3],
        MenuScope.Drives => BaseParents[4],
        MenuScope.FileExtension => @"SystemFileAssociations\" + command.Extension.ToLowerInvariant() + @"\shell",
        _ => throw new InvalidDataException("Unknown scope.")
    };
    public static string Invocation(MenuCommand command, string runnerPath)
    {
        if (runnerPath.IndexOfAny(['"', '\r', '\n', '%']) >= 0) throw new InvalidDataException("Install the application in a path without quotes, newlines, or percent signs.");
        var placeholder = command.Scope is MenuScope.FolderBackground or MenuScope.DesktopBackground ? "%V" : "%1";
        // Suffix keeps a drive root's trailing backslash from escaping the closing quote.
        return $"\"{runnerPath}\" --command {command.Id:D} --target \"{placeholder}{TargetSuffix}\"";
    }
    public static (Guid Id, string Target) ParseInvocation(string[] args)
    {
        if (args.Length != 4 || args[0] != "--command" || !Guid.TryParse(args[1], out var id) || args[2] != "--target" || !args[3].EndsWith(TargetSuffix, StringComparison.Ordinal))
            throw new ArgumentException("Expected --command <id> --target <path>|CMS.");
        var target = args[3][..^TargetSuffix.Length];
        if (string.IsNullOrWhiteSpace(target)) throw new ArgumentException("A selected file or folder is required.");
        return (id, target);
    }
    public static void ValidateTarget(MenuCommand command, string target)
    {
        bool file = File.Exists(target), directory = Directory.Exists(target);
        if (!file && !directory) throw new FileNotFoundException("The selected item no longer exists.", target);
        if (command.Scope is MenuScope.Files or MenuScope.FileExtension && !file) throw new ArgumentException("This command requires a file.");
        if (command.Scope is MenuScope.Folders or MenuScope.FolderBackground or MenuScope.DesktopBackground or MenuScope.Drives && !directory) throw new ArgumentException("This command requires a folder or drive.");
        if (command.Scope == MenuScope.FileExtension && !string.Equals(Path.GetExtension(target), command.Extension, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("The selected file does not match the configured extension.");
    }
}
