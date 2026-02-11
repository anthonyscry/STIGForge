using System.Text.Json.Serialization;

namespace STIGForge.Core.Models;

public enum OsTarget { Win11, Win10, Server2019, Server2022, Unknown }
public enum RoleTemplate { Workstation, MemberServer, DomainController, LabVm }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum HardeningMode { AuditOnly, Safe, Full }
public enum ClassificationMode { Classified, Unclassified, Mixed }

public enum ControlStatus { Pass, Fail, NotApplicable, Open, Conflict }
public enum ScopeTag { ClassifiedOnly, UnclassifiedOnly, Both, Unknown }
public enum Confidence { High, Medium, Low }
