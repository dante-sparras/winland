using System.Xml;

namespace Winland.Install;

public sealed record LogonTask(string Name, string Executable, string User, string Xml);

public static class LogonTaskDefinition
{
    public const string TaskName = "Winland";

    private const string Namespace = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    public static LogonTask Build(string executable, string user)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ArgumentException.ThrowIfNullOrWhiteSpace(user);

        return new LogonTask(TaskName, executable, user, WriteXml(executable, user));
    }

    private static string WriteXml(string executable, string user)
    {
        using var buffer = new StringWriter();
        using (var xml = XmlWriter.Create(buffer, new XmlWriterSettings { Indent = true }))
        {
            xml.WriteStartDocument();
            xml.WriteStartElement("Task", Namespace);
            xml.WriteAttributeString("version", "1.2");

            xml.WriteStartElement("RegistrationInfo", Namespace);
            xml.WriteElementString("Description", Namespace, "Start Winland at logon.");
            xml.WriteEndElement();

            xml.WriteStartElement("Triggers", Namespace);
            xml.WriteStartElement("LogonTrigger", Namespace);
            xml.WriteElementString("Enabled", Namespace, "true");
            xml.WriteElementString("UserId", Namespace, user);
            xml.WriteEndElement();
            xml.WriteEndElement();

            xml.WriteStartElement("Principals", Namespace);
            xml.WriteStartElement("Principal", Namespace);
            xml.WriteAttributeString("id", "Author");
            xml.WriteElementString("UserId", Namespace, user);
            xml.WriteElementString("LogonType", Namespace, "InteractiveToken");
            xml.WriteElementString("RunLevel", Namespace, "HighestAvailable");
            xml.WriteEndElement();
            xml.WriteEndElement();

            xml.WriteStartElement("Settings", Namespace);
            xml.WriteElementString("MultipleInstancesPolicy", Namespace, "IgnoreNew");
            xml.WriteElementString("DisallowStartIfOnBatteries", Namespace, "false");
            xml.WriteElementString("StopIfGoingOnBatteries", Namespace, "false");
            xml.WriteElementString("AllowHardTerminate", Namespace, "true");
            xml.WriteElementString("StartWhenAvailable", Namespace, "false");
            xml.WriteElementString("RunOnlyIfNetworkAvailable", Namespace, "false");
            xml.WriteElementString("AllowStartOnDemand", Namespace, "true");
            xml.WriteElementString("Enabled", Namespace, "true");
            xml.WriteElementString("Hidden", Namespace, "false");
            xml.WriteElementString("RunOnlyIfIdle", Namespace, "false");
            xml.WriteElementString("WakeToRun", Namespace, "false");
            xml.WriteElementString("ExecutionTimeLimit", Namespace, "PT0S");
            xml.WriteEndElement();

            xml.WriteStartElement("Actions", Namespace);
            xml.WriteAttributeString("Context", "Author");
            xml.WriteStartElement("Exec", Namespace);
            xml.WriteElementString("Command", Namespace, executable);
            xml.WriteEndElement();
            xml.WriteEndElement();

            xml.WriteEndElement();
            xml.WriteEndDocument();
        }

        return buffer.ToString();
    }
}
