namespace STIGForge.Core.Models;

public enum OsTarget { Win11, Server2019 }
public enum RoleTemplate { Workstation, MemberServer, DomainController, LabVm }
public enum HardeningMode { AuditOnly, Safe, Full }
public enum ClassificationMode { Classified, Unclassified, Mixed }

public enum ControlStatus { Pass, Fail, NotApplicable, Open, Conflict }
public enum ScopeTag { ClassifiedOnly, UnclassifiedOnly, Both, Unknown }
public enum Confidence { High, Medium, Low }
