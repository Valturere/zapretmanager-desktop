namespace ZapretManager.Models;

public sealed record GameModeOption(string Value, string Label)
{
    public override string ToString() => Label;
}
