namespace JobHunter.Application.Storage;

public interface IAppDataDirectory
{
    string RootPath { get; }

    string GetPath(string relativePath);
}
