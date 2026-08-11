namespace ProjectManagement.Services.Publications;

/// <summary>
/// Canonical publication-level content and fit policy for the original-format hard-copy brochure.
/// These values are deliberately independent of project records: an editor may change them for a
/// publication without mutating any project data, and can always restore the approved reference copy.
/// </summary>
public static class BrochurePrintPublicationPolicy
{
    public const int CentreStatementMaximumWords = 60;
    public const int OpeningNarrativeMaximumWords = 260;
    public const int FutureNarrativeMaximumWords = 180;
    public const int ProcurementMaximumWords = 190;
    public const int DevelopingAgencyMaximumWords = 90;
    public const int ManufacturingAgencyMaximumWords = 60;
    public const int VisionaryMaximumWords = 240;
    public const int NewSimulatorsMaximumWords = 100;

    public static BrochurePrintMatter ApprovedReference { get; } = new(
        CentreStatement: "SDD is the Centre of Expertise in AR/VR and the nodal centre for development of Simulators and Niche technologies in AI, Drones & Robotics.",
        OpeningNarrative: "Simulators represent a cornerstone of modern military training, serving as advanced force multipliers that harness cutting-edge technology to significantly enhance training effectiveness and overcome the inherent limitations of live exercises. The escalating complexity and cost of contemporary weapon systems, combined with ammunition scarcity, the imperative to preserve operational readiness, dynamic and fluid battle conditions, shrinking training spaces, and fiscal constraints, all drive the expanding integration of simulators across leading military forces worldwide. These sophisticated training platforms enable realistic preparation for high-risk and life-critical scenarios within a controlled and safe environment. They allow for repeated execution of complex manoeuvres on fully interactive systems, optimise resource utilisation, mitigate risks associated with live training accidents, and facilitate data-driven coaching alongside objective performance metrics. Recent technological advancements in electronics, computing, and immersive software have significantly enhanced realism, effectively narrowing the gap between live operational systems and their simulated counterparts, thereby elevating combat preparedness to new levels.",
        FutureNarrative: "This decade marks a phase of technological transformation, as advances reshape military operations through autonomous, integrated, and AI-enabled systems. The Indian Army is leading this evolution by adopting cutting-edge capabilities, with the Simulator Development Division gearing up to deliver aligned training and operational solutions. IA initiatives, such as Cyber Quest 2025, drive the integration of AI, machine learning, quantum computing, and drone technology to counter threats. Focus on cyber and electronic warfare, as well as advanced strike systems, signals a future-ready vision. Meanwhile, collaboration with academia and industry through schemes such as ADITI fosters indigenous innovation, modernisation, and battlefield self-reliance.",
        ProcurementGuidance: "Procurement of the simulators under revenue route can be done through appropriate grants like IR&D/ ACSFP/ATG/TTIEG/ etc by Units/ Formations/ Establishments. Statement of case with production cost ascertained from 515 Army Base Workshop (ABW) is processed by the users for approval of relevant CFA. On allotment of funds for procurement, the payment work order can be placed on 515 ABW through HQ Base Workshop Group (EME), Meerut Cantt. The funds have to be transferred from Unit CDA to CDA, Bengaluru, of 515 ABW. The required simulator is then manufactured by 515 ABW and subsequently installed at the unit premises along with training to unit. The simulators can also be procured through MOLTI/MOTIMS as per the policy in vogue.",
        DevelopingAgency: "Simulator Development Division,\nTrimulgherry Post, Secunderabad - 500015. Telangana.\nTele/Fax: 040-27794273 ; 040-27795418\nArmy Intranet Website : http://sdd.army.mil/\nE-mail ID: itsdd1234@gmail.com ; sdd.it@gov.in",
        ManufacturingAgency: "515 Army Base Workshop,\nBangalore-560008. Karnataka.\nTele/Fax: 080-25591567.\nArmy : 460108-6842",
        VisionaryHorizons: "Technological advances are reshaping modern warfare, integrating artificial intelligence, big data, drones, quantum technologies and autonomous systems to enhance efficiency, precision and speed. Contemporary battle strategies rely on UAVs, cyber operations and AI-driven decision support to sharpen intelligence and situational awareness. Future capabilities are centred on AI, robotics, quantum computing, blockchain, machine learning and next-generation communications, enabling autonomous platforms such as drone swarms and robotic vehicles that reduce human risk and improve accuracy. Within this landscape, the Indian Army is actively inducting emerging technologies. The Simulator Development Division is designing next-generation simulators, decision support tools, and testbeds that mirror these capabilities, preparing commanders and soldiers for technology-intensive, multi-domain operations. Ultimately, this proactive digital integration guarantees that frontline forces achieve complete cognitive dominance and tactical superiority long before ever stepping into the actual physical combat zone.",
        NewSimulatorsGuidance: "In case of requirements of new simulators/ niche technology products, HQ ARTRAC (AI & Simulation) may be approached along with Statement of Case covering detailed requirements. The requirement of simulators/ products may also be proposed during Simulator & Wargame Apex Committee Meeting as and when held.");

    public static BrochurePrintMatter FromOptions(BrochureBuildOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new BrochurePrintMatter(
            options.PrintCentreStatement,
            options.PrintIntroText,
            options.PrintFutureText,
            options.PrintProcurementText,
            options.PrintDevelopingAgencyText,
            options.PrintManufacturingAgencyText,
            options.PrintVisionaryText,
            options.PrintNewSimulatorsText);
    }

    public static IReadOnlyList<BrochurePreflightIssue> Validate(
        BrochurePublicationProfile profile,
        BrochurePrintMatter? matter)
    {
        if (profile != BrochurePublicationProfile.PrintCompact)
        {
            return Array.Empty<BrochurePreflightIssue>();
        }

        matter ??= new BrochurePrintMatter(null, null, null, null, null, null, null, null);
        var issues = new List<BrochurePreflightIssue>();

        ValidateSection(issues, matter.CentreStatement, CentreStatementMaximumWords, "Centre of Expertise statement");
        ValidateSection(issues, matter.OpeningNarrative, OpeningNarrativeMaximumWords, "Role of simulators / opening narrative");
        ValidateSection(issues, matter.FutureNarrative, FutureNarrativeMaximumWords, "Technology and future-readiness narrative");
        ValidateSection(issues, matter.ProcurementGuidance, ProcurementMaximumWords, "Procurement guidance");
        ValidateSection(issues, matter.DevelopingAgency, DevelopingAgencyMaximumWords, "Developing Agency / contact details");
        ValidateSection(issues, matter.ManufacturingAgency, ManufacturingAgencyMaximumWords, "Manufacturing Agency details");
        ValidateSection(issues, matter.VisionaryHorizons, VisionaryMaximumWords, "Visionary Horizons & Strategic Objectives");
        ValidateSection(issues, matter.NewSimulatorsGuidance, NewSimulatorsMaximumWords, "New Simulators guidance");

        return issues;
    }

    private static void ValidateSection(
        ICollection<BrochurePreflightIssue> issues,
        string? value,
        int maximumWords,
        string label)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            issues.Add(new BrochurePreflightIssue(
                BrochurePreflightIssueCode.PrintInstitutionalContentMissing,
                PublicationIssueSeverity.Blocker,
                null,
                null,
                $"{label} is required for the Print / Compact brochure."));
            return;
        }

        var wordCount = BrochureLayoutPlanner.CountWords(value);
        if (wordCount <= maximumWords)
        {
            return;
        }

        issues.Add(new BrochurePreflightIssue(
            BrochurePreflightIssueCode.PrintInstitutionalContentTooLong,
            PublicationIssueSeverity.Blocker,
            null,
            null,
            $"{label} is {wordCount} words. The compact hard-copy layout supports up to {maximumWords} words in this section."));
    }
}
