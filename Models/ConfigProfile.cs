namespace ZapretManager.Models;

public sealed record ConfigProfile(string Name, string FilePath)
{
    public string FileName => Path.GetFileName(FilePath);
}
