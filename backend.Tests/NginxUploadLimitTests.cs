using System.Text.RegularExpressions;

namespace ClubHub.Api.Tests;

public sealed class NginxUploadLimitTests
{
    [Fact]
    public void ApiProxy_AllowsLearningUploadRequestLimit()
    {
        var repositoryRoot = FindRepositoryRoot();
        var configuration = File.ReadAllText(Path.Combine(repositoryRoot, "nginx.conf"));

        Assert.Matches(
            new Regex(@"client_max_body_size\s+51m\s*;", RegexOptions.IgnoreCase),
            configuration);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "nginx.conf")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Cannot locate the repository nginx.conf file.");
    }
}
