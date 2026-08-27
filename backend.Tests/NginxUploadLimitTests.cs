using System.Text.RegularExpressions;

namespace ClubHub.Api.Tests;

public sealed class NginxUploadLimitTests
{
    [Fact]
    public void ApiProxy_AllowsLearningUploadRequestLimit()
    {
        var repositoryRoot = FindRepositoryRoot();
        var configuration = File.ReadAllText(Path.Combine(repositoryRoot, "nginx.conf"));

        var apiLocation = Regex.Match(
            configuration,
            @"(?mi)^[ \t]*location[ \t]+/api/[ \t]*\{(?<body>[\s\S]*)^[ \t]*\}");
        Assert.True(apiLocation.Success, "Cannot locate the /api/ location.");
        Assert.Matches(
            new Regex(
                @"(?mi)^[ \t]*client_max_body_size[ \t]+51m[ \t]*;[ \t]*(?:#.*)?$"),
            apiLocation.Groups["body"].Value);
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
