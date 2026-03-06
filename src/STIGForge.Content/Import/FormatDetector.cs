using System.Xml;

namespace STIGForge.Content.Import;

internal sealed class SourceArtifactStats
{
    public int XccdfXmlCount { get; set; }
    public int OvalXmlCount { get; set; }
    public int ScapDataStreamXmlCount { get; set; }
    public int AdmxCount { get; set; }
    public int TotalXmlCount { get; set; }
}

internal sealed class FormatDetector
{
    internal SourceArtifactStats CountSourceArtifacts(string extractedRoot)
    {
        var xmlPaths = Directory.EnumerateFiles(extractedRoot, "*.xml", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var xmlFiles = xmlPaths
            .Select(Path.GetFileName)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToList();

        var xccdf = GetXccdfCandidateXmlFiles(extractedRoot).Count;
        var oval = xmlFiles.Count(f => f!.IndexOf("oval", StringComparison.OrdinalIgnoreCase) >= 0);
        var scapDataStreams = xmlPaths.Count(path =>
            TryReadXmlRootLocalName(path, out var rootLocalName)
            && string.Equals(rootLocalName, "data-stream-collection", StringComparison.OrdinalIgnoreCase));
        var admx = Directory.EnumerateFiles(extractedRoot, "*.admx", SearchOption.AllDirectories).Count();

        return new SourceArtifactStats
        {
            XccdfXmlCount = xccdf,
            OvalXmlCount = oval,
            ScapDataStreamXmlCount = scapDataStreams,
            AdmxCount = admx,
            TotalXmlCount = xmlFiles.Count
        };
    }

    internal IReadOnlyList<string> GetXccdfCandidateXmlFiles(string root)
    {
        return Directory.EnumerateFiles(root, "*.xml", SearchOption.AllDirectories)
            .Where(path =>
            {
                var fileName = Path.GetFileName(path);
                if (fileName.IndexOf("xccdf", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

                if (!TryReadXmlRootLocalName(path, out var rootLocalName))
                    return false;

                return string.Equals(rootLocalName, "Benchmark", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(rootLocalName, "data-stream-collection", StringComparison.OrdinalIgnoreCase);
            })
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    internal bool TryReadXmlRootLocalName(string xmlPath, out string rootLocalName)
    {
        rootLocalName = string.Empty;

        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                IgnoreWhitespace = true,
                IgnoreComments = true,
                XmlResolver = null
            };

            using var reader = XmlReader.Create(xmlPath, settings);
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element)
                    continue;

                rootLocalName = reader.LocalName;
                return !string.IsNullOrWhiteSpace(rootLocalName);
            }
        }
        catch (Exception)
        {
        }

        return false;
    }

    internal FormatDetectionResult DetectPackFormatWithConfidence(string extractedRoot, SourceArtifactStats stats)
    {
        _ = extractedRoot;
        var result = new FormatDetectionResult
        {
            Format = PackFormat.Unknown,
            Confidence = DetectionConfidence.Low
        };

        var hasXccdf = stats.XccdfXmlCount > 0;
        var hasOval = stats.OvalXmlCount > 0;
        var hasDataStream = stats.ScapDataStreamXmlCount > 0;
        var hasAdmx = stats.AdmxCount > 0;

        if (hasXccdf && (hasOval || hasDataStream))
        {
            result.Format = PackFormat.Scap;
            result.Confidence = DetectionConfidence.High;

            if (hasOval && hasDataStream)
            {
                result.Reasons.Add($"Found {stats.XccdfXmlCount} XCCDF, {stats.OvalXmlCount} OVAL, and {stats.ScapDataStreamXmlCount} SCAP data stream XML files - characteristic SCAP bundle signature.");
            }
            else if (hasOval)
            {
                result.Reasons.Add($"Found {stats.XccdfXmlCount} XCCDF and {stats.OvalXmlCount} OVAL files - characteristic SCAP bundle signature.");
            }
            else
            {
                result.Reasons.Add($"Found {stats.XccdfXmlCount} XCCDF files including {stats.ScapDataStreamXmlCount} SCAP data stream XML file(s) - characteristic SCAP bundle signature.");
            }

            return result;
        }

        if (hasAdmx)
        {
            result.Format = PackFormat.Gpo;
            result.Confidence = hasXccdf || hasOval ? DetectionConfidence.Medium : DetectionConfidence.High;
            result.Reasons.Add($"Found {stats.AdmxCount} ADMX files - GPO policy format.");
            if (hasXccdf || hasOval)
                result.Reasons.Add("Warning: XCCDF/OVAL files also present - possible mixed-format bundle.");
            return result;
        }

        if (hasXccdf)
        {
            result.Format = PackFormat.Stig;
            result.Confidence = DetectionConfidence.High;
            result.Reasons.Add($"Found {stats.XccdfXmlCount} XCCDF files with no OVAL - standalone STIG benchmark.");
            return result;
        }

        result.Format = PackFormat.Unknown;
        result.Confidence = DetectionConfidence.Low;
        result.Reasons.Add($"No XCCDF, OVAL, or ADMX files detected in {stats.TotalXmlCount} total XML files.");
        result.Reasons.Add("Will attempt STIG parser as fallback with low confidence.");
        return result;
    }
}
