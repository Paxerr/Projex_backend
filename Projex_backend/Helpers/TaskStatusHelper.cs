namespace Projex_backend.Helpers
{
    public static class TaskStatusHelper
    {
        public static readonly string[] AllowedStatuses = ["Assigned", "InProgress", "Done"];

        public static string Normalize(string? status)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                return "Assigned";
            }

            return status.Trim().ToLowerInvariant() switch
            {
                "todo" => "Assigned",
                "to do" => "Assigned",
                "assigned" => "Assigned",
                "đã nhận" => "Assigned",
                "inprogress" => "InProgress",
                "in progress" => "InProgress",
                "đang thực hiện" => "InProgress",
                "done" => "Done",
                "completed" => "Done",
                "hoàn thành" => "Done",
                _ => status.Trim()
            };
        }

        public static bool IsValid(string? status)
        {
            return AllowedStatuses.Contains(Normalize(status), StringComparer.OrdinalIgnoreCase);
        }
    }
}
