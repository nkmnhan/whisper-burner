using Microsoft.Build.Framework;

namespace Microsoft.Build.AppxPackage
{
    public abstract class StubTask : ITask
    {
        public IBuildEngine BuildEngine { get; set; } = null!;
        public ITaskHost HostObject { get; set; } = null!;
        public abstract bool Execute();
    }

    public class ExpandPayloadDirectories : StubTask
    {
        public ITaskItem[]? Inputs { get; set; }
        public string? VsTelemetrySession { get; set; }
        [Output] public ITaskItem[] Expanded { get; set; } = [];
        public override bool Execute() { Expanded = Inputs ?? []; return true; }
    }

    public class RemovePayloadDuplicates : StubTask
    {
        public ITaskItem[]? Inputs { get; set; }
        public string? ProjectName { get; set; }
        public string? Platform { get; set; }
        public string? VsTelemetrySession { get; set; }
        [Output] public ITaskItem[] Filtered { get; set; } = [];
        public override bool Execute() { Filtered = Inputs ?? []; return true; }
    }

    public class GetDefaultResourceLanguage : StubTask
    {
        public string? DefaultLanguage { get; set; }
        public ITaskItem[]? SourceAppxManifest { get; set; }
        public string? VsTelemetrySession { get; set; }
        [Output] public string DefaultResourceLanguage { get; set; } = "en-US";
        public override bool Execute() { DefaultResourceLanguage = string.IsNullOrEmpty(DefaultLanguage) ? "en-US" : DefaultLanguage!; return true; }
    }

    public class GetPackageArchitecture : StubTask
    {
        public string? Platform { get; set; }
        [Output] public string PackageArchitecture { get; set; } = "x64";
        public override bool Execute() { PackageArchitecture = "x64"; return true; }
    }

    public class GetSdkFileFullPath : StubTask
    {
        public string? FileName { get; set; }
        public string? FullFilePath { get; set; }
        [Output] public string ReturnedFullPath { get; set; } = "";
        public override bool Execute() { ReturnedFullPath = ""; return true; }
    }

    public class GetSdkPropertyValue : StubTask
    {
        public string? PropertyName { get; set; }
        [Output] public string PropertyValue { get; set; } = "";
        public override bool Execute() { PropertyValue = ""; return true; }
    }

    public class RemoveRedundantXamlFilesFromSdkPayload : StubTask
    {
        public ITaskItem[]? InputFiles { get; set; }
        public ITaskItem[]? SdkFiles { get; set; }
        [Output] public ITaskItem[] OutputFiles { get; set; } = [];
        public override bool Execute() { OutputFiles = InputFiles ?? []; return true; }
    }

    public class ValidateConfiguration : StubTask
    {
        public string? Platform { get; set; }
        public string? Configuration { get; set; }
        public override bool Execute() { return true; }
    }
}
