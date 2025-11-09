using System.Linq;
using System.Text;

namespace ProjectManagement.Services.Projects
{
    public sealed class OngoingProjectsExcelBuilder : IOngoingProjectsExcelBuilder
    {
        public byte[] Build(OngoingProjectsExportContext context)
        {
            var sb = new StringBuilder();

            sb.Append("Project,Category,Current stage,Last completed stage,Last completed date");

            foreach (var code in ProjectManagement.Models.Stages.StageCodes.All)
            {
                sb.Append(',');
                sb.Append(code);
                sb.Append(" status");

                sb.Append(',');
                sb.Append(code);
                sb.Append(" actual");

                sb.Append(',');
                sb.Append(code);
                sb.Append(" PDC");
            }

            sb.AppendLine();

            foreach (var item in context.Items)
            {
                sb.Append(E(item.ProjectName)); sb.Append(',');
                sb.Append(E(item.ProjectCategoryName ?? "")); sb.Append(',');
                sb.Append(E(item.CurrentStageName ?? "")); sb.Append(',');
                sb.Append(E(item.LastCompletedStageName ?? "")); sb.Append(',');
                sb.Append(item.LastCompletedStageDate?.ToString("dd-MMM-yyyy") ?? "");

                foreach (var code in ProjectManagement.Models.Stages.StageCodes.All)
                {
                    var stage = item.Stages.FirstOrDefault(s => s.Code == code);
                    if (stage == null)
                    {
                        sb.Append(",,,");
                        continue;
                    }

                    sb.Append(',');
                    sb.Append(stage.Status.ToString());

                    sb.Append(',');
                    sb.Append(stage.ActualCompletedOn?.ToString("dd-MMM-yyyy") ?? "");

                    sb.Append(',');
                    sb.Append(stage.PlannedDue?.ToString("dd-MMM-yyyy") ?? "");
                }

                sb.AppendLine();
            }

            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        private static string E(string v)
        {
            if (string.IsNullOrEmpty(v)) return "";
            if (v.Contains(",") || v.Contains("\""))
            {
                return "\"" + v.Replace("\"", "\"\"") + "\"";
            }
            return v;
        }
    }
}
