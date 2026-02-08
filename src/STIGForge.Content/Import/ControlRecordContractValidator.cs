using STIGForge.Core.Models;

namespace STIGForge.Content.Import;

public static class ControlRecordContractValidator
{
  public static List<string> Validate(IReadOnlyList<ControlRecord> controls)
  {
    var errors = new List<string>();

    for (var i = 0; i < controls.Count; i++)
    {
      var control = controls[i];
      var key = !string.IsNullOrWhiteSpace(control.ControlId) ? control.ControlId : $"index:{i}";

      if (string.IsNullOrWhiteSpace(control.ControlId))
        errors.Add($"{key}: missing control_id");

      if (string.IsNullOrWhiteSpace(control.Title))
        errors.Add($"{key}: missing title");

      if (string.IsNullOrWhiteSpace(control.Severity))
        errors.Add($"{key}: missing severity");

      if (control.ExternalIds == null)
        errors.Add($"{key}: missing external_ids");

      if (control.Applicability == null)
        errors.Add($"{key}: missing applicability");

      if (control.Revision == null)
      {
        errors.Add($"{key}: missing revision");
      }
      else if (string.IsNullOrWhiteSpace(control.Revision.PackName))
      {
        errors.Add($"{key}: missing revision.pack_name");
      }
    }

    return errors;
  }
}
