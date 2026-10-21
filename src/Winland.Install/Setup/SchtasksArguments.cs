namespace Winland.Install;

public static class SchtasksArguments
{
    public static IReadOnlyList<string> Query(string taskName) => ["/Query", "/TN", taskName];

    public static IReadOnlyList<string> Create(string taskName, string xmlPath) =>
        ["/Create", "/TN", taskName, "/XML", xmlPath];

    public static IReadOnlyList<string> Update(string taskName, string xmlPath) =>
        ["/Create", "/TN", taskName, "/XML", xmlPath, "/F"];

    public static IReadOnlyList<string> Delete(string taskName) => ["/Delete", "/TN", taskName, "/F"];
}
