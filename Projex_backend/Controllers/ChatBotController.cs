using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Projex_backend.Data;
using Projex_backend.Helpers;

namespace Projex_backend.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/chatbot")]
    public class ChatBotController : ControllerBase
    {
        private const int MaxLimit = 50;
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        {
            WriteIndented = false
        };

        private readonly AppDbContext _db;
        private readonly IConfiguration _config;
        private readonly IHttpClientFactory _httpClientFactory;

        public ChatBotController(AppDbContext db, IConfiguration config, IHttpClientFactory httpClientFactory)
        {
            _db = db;
            _config = config;
            _httpClientFactory = httpClientFactory;
        }

        [HttpPost]
        public async Task<IActionResult> Chat([FromBody] ChatbotRequest request)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            var apiKey = _config["Gemini:ApiKey"];
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new
                {
                    message = "Chưa cấu hình Gemini API key. Hãy cấu hình Gemini:ApiKey hoặc Gemini__ApiKey."
                });
            }

            var currentUserId = User.GetUserId();
            var currentUser = await _db.Users
                .AsNoTracking()
                .Where(x => x.Id == currentUserId)
                .Select(x => new { x.Id, x.Email, x.FullName })
                .FirstOrDefaultAsync();

            if (currentUser == null)
            {
                return Unauthorized(new { message = "Không tìm thấy người dùng." });
            }

            var memberships = await _db.ProjectMembers
                .AsNoTracking()
                .Where(x => x.UserId == currentUserId)
                .Select(x => new { x.ProjectId, x.Role })
                .ToListAsync();

            var context = new ChatbotUserContext
            {
                CurrentUserId = currentUser.Id,
                Email = currentUser.Email,
                FullName = currentUser.FullName,
                AccessibleProjectIds = memberships.Select(x => x.ProjectId).Distinct().ToList(),
                ManageableProjectIds = memberships
                    .Where(x => x.Role == "Owner" || x.Role == "Admin")
                    .Select(x => x.ProjectId)
                    .Distinct()
                    .ToList(),
                RolesByProject = memberships.ToDictionary(x => x.ProjectId.ToString(), x => x.Role)
            };

            ChatbotQueryPlan plan;
            try
            {
                plan = await CreateQueryPlanAsync(request.Question.Trim(), context, apiKey);
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status502BadGateway, new
                {
                    message = "Không thể tạo kế hoạch truy vấn cho chatbot.",
                    detail = ex.Message
                });
            }

            var validation = ValidatePlan(plan, context);
            if (!validation.IsValid)
            {
                return BadRequest(new
                {
                    message = validation.Message,
                    plan
                });
            }

            if (!plan.Allowed)
            {
                return Ok(new ChatbotResponse
                {
                    Answer = plan.DenyReason ?? "Tôi không thể truy cập dữ liệu này.",
                    Intent = plan.Intent,
                    Plan = plan,
                    Data = null
                });
            }

            if (plan.NeedsClarification)
            {
                return Ok(new ChatbotResponse
                {
                    Answer = plan.ClarificationQuestion ?? "Bạn cần cung cấp thêm thông tin để tôi trả lời chính xác.",
                    Intent = plan.Intent,
                    Plan = plan,
                    Data = null
                });
            }

            object data;
            try
            {
                data = await ExecutePlanAsync(plan, context);
            }
            catch (Exception ex)
            {
                return BadRequest(new
                {
                    message = "Không thể thực thi kế hoạch truy vấn của chatbot.",
                    detail = ex.Message,
                    plan
                });
            }

            string answer;
            try
            {
                answer = await CreateFinalAnswerAsync(request.Question.Trim(), data, plan, apiKey);
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status502BadGateway, new
                {
                    message = "Không thể tạo câu trả lời cuối cho chatbot.",
                    detail = ex.Message,
                    plan,
                    data
                });
            }

            return Ok(new ChatbotResponse
            {
                Answer = answer,
                Intent = plan.Intent,
                Plan = plan,
                Data = data
            });
        }

        private async Task<ChatbotQueryPlan> CreateQueryPlanAsync(
            string question,
            ChatbotUserContext context,
            string apiKey)
        {
            var prompt = $$"""
            USER_QUESTION:
            {{question}}

            CURRENT_DATE:
            {{DateTime.Now:yyyy-MM-dd}}

            CURRENT_USER_CONTEXT:
            {{JsonSerializer.Serialize(context, JsonOptions)}}

            DB_SCHEMA:
            {{GetDbSchema()}}

            CÁC INTENT ĐƯỢC HỖ TRỢ:
            - my_dashboard_summary: tổng quan dashboard của người dùng hiện tại.
            - count_my_overdue_tasks: đếm task bị trễ được giao cho người dùng hiện tại.
            - list_my_tasks: liệt kê task được giao cho người dùng hiện tại.
            - list_my_created_tasks: liệt kê task do người dùng hiện tại tạo.
            - list_my_notifications: liệt kê thông báo của người dùng hiện tại.
            - list_my_projects: liệt kê project mà người dùng hiện tại tham gia.
            - project_summary: tóm tắt một project mà người dùng có quyền truy cập theo projectId, projectCode hoặc projectName.
            - list_project_tasks: liệt kê task trong một project mà người dùng có quyền truy cập.
            - list_project_members: liệt kê thành viên trong một project mà người dùng có quyền truy cập.
            - managed_project_workload: thống kê workload hoặc task trễ theo thành viên, chỉ dành cho Owner/Admin của project.

            QUY TẮC:
            1. Chỉ trả về JSON. Không trả lời trực tiếp người dùng.
            2. Chọn đúng một intent trong danh sách được hỗ trợ.
            3. Nếu câu hỏi yêu cầu số liệu cá nhân của người khác, chỉ cho phép intent managed_project_workload khi có project cụ thể mà current user quản lý.
            4. Nếu câu hỏi cần project nhưng chưa xác định được duy nhất một project, đặt needsClarification=true.
            5. Không bao giờ yêu cầu PasswordHash, PasswordResetTokens, JWT/token, mật khẩu hoặc mã reset.
            6. Notifications chỉ được lấy cho currentUserId.
            7. Luôn loại task đã xóa.
            8. Task trễ nghĩa là DueDate != null, DueDate < CURRENT_DATE và Status != Done.
            9. Trả projectCode/projectName đúng như người dùng nhắc tới nếu có; không tự bịa projectId.
            10. Giới hạn tối đa là {{MaxLimit}}.

            ĐỊNH DẠNG JSON BẮT BUỘC:
            {
              "allowed": true,
              "denyReason": null,
              "needsClarification": false,
              "clarificationQuestion": null,
              "intent": "one_supported_intent",
              "riskLevel": "low",
              "parameters": {
                "projectId": null,
                "projectCode": null,
                "projectName": null,
                "status": null,
                "includeOverdueOnly": false,
                "limit": 20
              },
              "answerPlan": "kế hoạch trả lời ngắn gọn"
            }
            """;

            var responseText = await CreateGeminiTextResponseAsync(
                apiKey,
                "Bạn là bộ lập kế hoạch truy vấn JSON nghiêm ngặt cho chatbot quản lý dự án.",
                prompt,
                requireJson: true);

            var plan = JsonSerializer.Deserialize<ChatbotQueryPlan>(responseText, JsonOptions);
            if (plan == null)
            {
                throw new InvalidOperationException("Model trả về kế hoạch truy vấn rỗng.");
            }

            return plan;
        }

        private async Task<string> CreateFinalAnswerAsync(
            string question,
            object data,
            ChatbotQueryPlan plan,
            string apiKey)
        {
            var prompt = $$"""
            Bạn là chatbot AI của Projex.
            Trả lời bằng tiếng Việt, ngắn gọn, tự nhiên.
            Chỉ dùng dữ liệu backend cung cấp.
            Nếu data rỗng hoặc không đủ, nói rõ là chưa có dữ liệu phù hợp.

            USER_QUESTION:
            {{question}}

            INTENT:
            {{plan.Intent}}

            BACKEND_DATA:
            {{JsonSerializer.Serialize(data, JsonOptions)}}
            """;

            return await CreateGeminiTextResponseAsync(apiKey, "Bạn trả lời với vai trò trợ lý Projex.", prompt, requireJson: false);
        }

        private async Task<string> CreateGeminiTextResponseAsync(
            string apiKey,
            string instructions,
            string input,
            bool requireJson)
        {
            var model = _config["Gemini:Model"] ?? "gemini-2.5-flash";
            var client = _httpClientFactory.CreateClient();
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(model)}:generateContent";
            using var message = new HttpRequestMessage(HttpMethod.Post, url);
            message.Headers.Add("x-goog-api-key", apiKey);

            var body = new Dictionary<string, object?>
            {
                ["systemInstruction"] = new
                {
                    parts = new[]
                    {
                        new { text = instructions }
                    }
                },
                ["contents"] = new[]
                {
                    new
                    {
                        role = "user",
                        parts = new[]
                        {
                            new { text = input }
                        }
                    }
                },
                ["generationConfig"] = new Dictionary<string, object?>
                {
                    ["temperature"] = 0.2
                }
            };

            if (requireJson)
            {
                ((Dictionary<string, object?>)body["generationConfig"]!)["responseMimeType"] = "application/json";
            }

            message.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");

            using var response = await client.SendAsync(message);
            var content = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Gemini API trả lỗi {(int)response.StatusCode}: {content}");
            }

            var outputText = ExtractGeminiOutputText(content);
            if (string.IsNullOrWhiteSpace(outputText))
            {
                throw new InvalidOperationException("Phản hồi Gemini không có nội dung văn bản.");
            }

            return outputText.Trim();
        }

        private static string ExtractGeminiOutputText(string responseJson)
        {
            using var document = JsonDocument.Parse(responseJson);
            var root = document.RootElement;

            if (!root.TryGetProperty("candidates", out var candidates) ||
                candidates.ValueKind != JsonValueKind.Array ||
                candidates.GetArrayLength() == 0)
            {
                return string.Empty;
            }

            var candidate = candidates[0];
            if (!candidate.TryGetProperty("content", out var content) ||
                !content.TryGetProperty("parts", out var parts) ||
                parts.ValueKind != JsonValueKind.Array)
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            foreach (var part in parts.EnumerateArray())
            {
                if (part.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                {
                    builder.Append(text.GetString());
                }
            }

            return builder.ToString();
        }

        private PlanValidationResult ValidatePlan(ChatbotQueryPlan plan, ChatbotUserContext context)
        {
            var supportedIntents = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "my_dashboard_summary",
                "count_my_overdue_tasks",
                "list_my_tasks",
                "list_my_created_tasks",
                "list_my_notifications",
                "list_my_projects",
                "project_summary",
                "list_project_tasks",
                "list_project_members",
                "managed_project_workload"
            };

            if (string.IsNullOrWhiteSpace(plan.Intent) || !supportedIntents.Contains(plan.Intent))
            {
                return PlanValidationResult.Invalid("Intent chatbot không được hỗ trợ.");
            }

            plan.Parameters ??= new ChatbotPlanParameters();
            plan.Parameters.Limit = Math.Clamp(plan.Parameters.Limit <= 0 ? 20 : plan.Parameters.Limit, 1, MaxLimit);

            if (!plan.Allowed || plan.NeedsClarification)
            {
                return PlanValidationResult.Valid();
            }

            if (plan.Intent is "project_summary" or "list_project_tasks" or "list_project_members")
            {
                var hasProjectSelector =
                    plan.Parameters.ProjectId.HasValue ||
                    !string.IsNullOrWhiteSpace(plan.Parameters.ProjectCode) ||
                    !string.IsNullOrWhiteSpace(plan.Parameters.ProjectName);

                if (!hasProjectSelector)
                {
                    return PlanValidationResult.Invalid("Intent theo project cần có thông tin xác định project.");
                }
            }

            if (plan.Intent == "managed_project_workload")
            {
                var hasProjectSelector =
                    plan.Parameters.ProjectId.HasValue ||
                    !string.IsNullOrWhiteSpace(plan.Parameters.ProjectCode) ||
                    !string.IsNullOrWhiteSpace(plan.Parameters.ProjectName);

                if (!hasProjectSelector)
                {
                    return PlanValidationResult.Invalid("Intent quản lý project cần có thông tin xác định project.");
                }

                if (context.ManageableProjectIds.Count == 0)
                {
                    return PlanValidationResult.Invalid("Người dùng hiện tại không quản lý project nào.");
                }
            }

            return PlanValidationResult.Valid();
        }

        private async Task<object> ExecutePlanAsync(ChatbotQueryPlan plan, ChatbotUserContext context)
        {
            return plan.Intent switch
            {
                "my_dashboard_summary" => await GetMyDashboardSummaryAsync(context),
                "count_my_overdue_tasks" => await CountMyOverdueTasksAsync(context),
                "list_my_tasks" => await ListMyTasksAsync(context, plan.Parameters!),
                "list_my_created_tasks" => await ListMyCreatedTasksAsync(context, plan.Parameters!),
                "list_my_notifications" => await ListMyNotificationsAsync(context, plan.Parameters!),
                "list_my_projects" => await ListMyProjectsAsync(context, plan.Parameters!),
                "project_summary" => await GetProjectSummaryAsync(context, plan.Parameters!, requireManageRole: false),
                "list_project_tasks" => await ListProjectTasksAsync(context, plan.Parameters!),
                "list_project_members" => await ListProjectMembersAsync(context, plan.Parameters!),
                "managed_project_workload" => await GetManagedProjectWorkloadAsync(context, plan.Parameters!),
                _ => throw new InvalidOperationException("Intent chatbot không được hỗ trợ.")
            };
        }

        private async Task<object> GetMyDashboardSummaryAsync(ChatbotUserContext context)
        {
            var myTaskIds = _db.TaskAssignments
                .Where(x => x.UserId == context.CurrentUserId)
                .Select(x => x.TaskId);

            var doneTasks = await _db.Tasks
                .AsNoTracking()
                .Where(x => myTaskIds.Contains(x.Id) && !x.IsDeleted && x.Status == "Done")
                .Select(x => new { x.DueDate, x.UpdatedAt })
                .ToListAsync();

            var onTimeRate = doneTasks.Count == 0
                ? 100
                : Math.Round(doneTasks.Count(x => x.DueDate == null || x.UpdatedAt == null || x.UpdatedAt <= x.DueDate) * 100d / doneTasks.Count, 2);

            return new
            {
                myProjects = await _db.ProjectMembers.CountAsync(x => x.UserId == context.CurrentUserId),
                myTasks = await _db.Tasks.CountAsync(x => myTaskIds.Contains(x.Id) && !x.IsDeleted),
                inProgressTasks = await _db.Tasks.CountAsync(x => myTaskIds.Contains(x.Id) && !x.IsDeleted && x.Status == "InProgress"),
                completedTasks = doneTasks.Count,
                overdueTasks = await _db.Tasks.CountAsync(x => myTaskIds.Contains(x.Id) && !x.IsDeleted && x.DueDate != null && x.DueDate < DateTime.Now && x.Status != "Done"),
                unreadNotifications = await _db.Notifications.CountAsync(x => x.UserId == context.CurrentUserId && !x.IsRead),
                onTimeRate
            };
        }

        private async Task<object> CountMyOverdueTasksAsync(ChatbotUserContext context)
        {
            var count = await _db.Tasks
                .AsNoTracking()
                .Where(t =>
                    !t.IsDeleted &&
                    t.DueDate != null &&
                    t.DueDate < DateTime.Now &&
                    t.Status != "Done" &&
                    context.AccessibleProjectIds.Contains(t.ProjectId) &&
                    t.Assignments.Any(a => a.UserId == context.CurrentUserId))
                .CountAsync();

            return new { overdueTaskCount = count };
        }

        private async Task<object> ListMyTasksAsync(ChatbotUserContext context, ChatbotPlanParameters parameters)
        {
            var query = _db.Tasks
                .AsNoTracking()
                .Where(t =>
                    !t.IsDeleted &&
                    context.AccessibleProjectIds.Contains(t.ProjectId) &&
                    t.Assignments.Any(a => a.UserId == context.CurrentUserId));

            query = ApplyTaskPlanFilters(query, parameters);

            return await query
                .OrderBy(t => t.DueDate ?? DateTime.MaxValue)
                .ThenByDescending(t => t.Id)
                .Take(parameters.Limit)
                .Select(t => new
                {
                    t.Id,
                    t.Title,
                    t.Status,
                    t.Priority,
                    t.DueDate,
                    Project = new { t.Project.Id, t.Project.Name, t.Project.Code }
                })
                .ToListAsync();
        }

        private async Task<object> ListMyCreatedTasksAsync(ChatbotUserContext context, ChatbotPlanParameters parameters)
        {
            var query = _db.Tasks
                .AsNoTracking()
                .Where(t =>
                    !t.IsDeleted &&
                    context.AccessibleProjectIds.Contains(t.ProjectId) &&
                    t.CreatedBy == context.CurrentUserId);

            query = ApplyTaskPlanFilters(query, parameters);

            return await query
                .OrderByDescending(t => t.Id)
                .Take(parameters.Limit)
                .Select(t => new
                {
                    t.Id,
                    t.Title,
                    t.Status,
                    t.Priority,
                    t.DueDate,
                    Project = new { t.Project.Id, t.Project.Name, t.Project.Code }
                })
                .ToListAsync();
        }

        private async Task<object> ListMyNotificationsAsync(ChatbotUserContext context, ChatbotPlanParameters parameters)
        {
            return await _db.Notifications
                .AsNoTracking()
                .Where(x => x.UserId == context.CurrentUserId)
                .OrderByDescending(x => x.CreatedAt)
                .Take(parameters.Limit)
                .Select(x => new
                {
                    x.Id,
                    x.Title,
                    x.Message,
                    x.Type,
                    x.IsRead,
                    x.ProjectId,
                    x.TaskId,
                    x.CreatedAt
                })
                .ToListAsync();
        }

        private async Task<object> ListMyProjectsAsync(ChatbotUserContext context, ChatbotPlanParameters parameters)
        {
            return await _db.Projects
                .AsNoTracking()
                .Where(p => context.AccessibleProjectIds.Contains(p.Id))
                .OrderByDescending(p => p.Id)
                .Take(parameters.Limit)
                .Select(p => new
                {
                    p.Id,
                    p.Name,
                    p.Code,
                    p.Description,
                    p.Status,
                    p.StartDate,
                    p.EndDate,
                    p.OwnerId,
                    MemberCount = p.Members.Count,
                    TaskCount = p.Tasks.Count(t => !t.IsDeleted)
                })
                .ToListAsync();
        }

        private async Task<object> GetProjectSummaryAsync(ChatbotUserContext context, ChatbotPlanParameters parameters, bool requireManageRole)
        {
            var project = await ResolveProjectAsync(context, parameters, requireManageRole);

            return new
            {
                Project = project,
                TotalTasks = await _db.Tasks.CountAsync(x => x.ProjectId == project.Id && !x.IsDeleted),
                AssignedTasks = await _db.Tasks.CountAsync(x => x.ProjectId == project.Id && !x.IsDeleted && x.Status == "Assigned"),
                InProgressTasks = await _db.Tasks.CountAsync(x => x.ProjectId == project.Id && !x.IsDeleted && x.Status == "InProgress"),
                DoneTasks = await _db.Tasks.CountAsync(x => x.ProjectId == project.Id && !x.IsDeleted && x.Status == "Done"),
                OverdueTasks = await _db.Tasks.CountAsync(x => x.ProjectId == project.Id && !x.IsDeleted && x.DueDate != null && x.DueDate < DateTime.Now && x.Status != "Done"),
                Members = await _db.ProjectMembers.CountAsync(x => x.ProjectId == project.Id)
            };
        }

        private async Task<object> ListProjectTasksAsync(ChatbotUserContext context, ChatbotPlanParameters parameters)
        {
            var project = await ResolveProjectAsync(context, parameters, requireManageRole: false);
            var query = _db.Tasks
                .AsNoTracking()
                .Where(t => t.ProjectId == project.Id && !t.IsDeleted);

            query = ApplyTaskPlanFilters(query, parameters);

            var tasks = await query
                .OrderBy(t => t.DueDate ?? DateTime.MaxValue)
                .ThenByDescending(t => t.Id)
                .Take(parameters.Limit)
                .Select(t => new
                {
                    t.Id,
                    t.Title,
                    t.Description,
                    t.Status,
                    t.Priority,
                    t.DueDate,
                    t.CreatedBy,
                    Assignees = t.Assignments.Select(a => new
                    {
                        a.UserId,
                        a.User.FullName,
                        a.User.Email
                    })
                })
                .ToListAsync();

            return new { Project = project, Tasks = tasks };
        }

        private async Task<object> ListProjectMembersAsync(ChatbotUserContext context, ChatbotPlanParameters parameters)
        {
            var project = await ResolveProjectAsync(context, parameters, requireManageRole: false);
            var members = await _db.ProjectMembers
                .AsNoTracking()
                .Where(x => x.ProjectId == project.Id)
                .OrderBy(x => x.User.FullName)
                .Take(parameters.Limit)
                .Select(x => new
                {
                    x.UserId,
                    x.Role,
                    x.JoinedAt,
                    User = new
                    {
                        x.User.Id,
                        x.User.FullName,
                        x.User.Email,
                        x.User.AvatarUrl
                    }
                })
                .ToListAsync();

            return new { Project = project, Members = members };
        }

        private async Task<object> GetManagedProjectWorkloadAsync(ChatbotUserContext context, ChatbotPlanParameters parameters)
        {
            var project = await ResolveProjectAsync(context, parameters, requireManageRole: true);
            var workload = await _db.TaskAssignments
                .AsNoTracking()
                .Where(a => a.Task.ProjectId == project.Id && !a.Task.IsDeleted)
                .GroupBy(a => new { a.UserId, a.User.FullName, a.User.Email })
                .Select(g => new
                {
                    g.Key.UserId,
                    g.Key.FullName,
                    g.Key.Email,
                    AssignedTaskCount = g.Count(),
                    InProgressTaskCount = g.Count(a => a.Task.Status == "InProgress"),
                    DoneTaskCount = g.Count(a => a.Task.Status == "Done"),
                    OverdueTaskCount = g.Count(a => a.Task.DueDate != null && a.Task.DueDate < DateTime.Now && a.Task.Status != "Done")
                })
                .OrderByDescending(x => x.OverdueTaskCount)
                .ThenByDescending(x => x.AssignedTaskCount)
                .Take(parameters.Limit)
                .ToListAsync();

            return new { Project = project, Workload = workload };
        }

        private IQueryable<Models.TaskItem> ApplyTaskPlanFilters(IQueryable<Models.TaskItem> query, ChatbotPlanParameters parameters)
        {
            if (!string.IsNullOrWhiteSpace(parameters.Status) && TaskStatusHelper.IsValid(parameters.Status))
            {
                var status = TaskStatusHelper.Normalize(parameters.Status);
                query = query.Where(t => t.Status == status);
            }

            if (parameters.IncludeOverdueOnly)
            {
                query = query.Where(t => t.DueDate != null && t.DueDate < DateTime.Now && t.Status != "Done");
            }

            return query;
        }

        private async Task<ProjectLookupResult> ResolveProjectAsync(
            ChatbotUserContext context,
            ChatbotPlanParameters parameters,
            bool requireManageRole)
        {
            var allowedProjectIds = requireManageRole ? context.ManageableProjectIds : context.AccessibleProjectIds;

            var query = _db.Projects
                .AsNoTracking()
                .Where(p => allowedProjectIds.Contains(p.Id));

            if (parameters.ProjectId.HasValue)
            {
                query = query.Where(p => p.Id == parameters.ProjectId.Value);
            }
            else if (!string.IsNullOrWhiteSpace(parameters.ProjectCode))
            {
                var code = parameters.ProjectCode.Trim();
                query = query.Where(p => p.Code == code);
            }
            else if (!string.IsNullOrWhiteSpace(parameters.ProjectName))
            {
                var name = parameters.ProjectName.Trim();
                query = query.Where(p => p.Name.Contains(name));
            }
            else
            {
                throw new InvalidOperationException("Cần có thông tin xác định project.");
            }

            var projects = await query
                .OrderByDescending(p => p.Id)
                .Take(2)
                .Select(p => new ProjectLookupResult
                {
                    Id = p.Id,
                    Name = p.Name,
                    Code = p.Code,
                    Status = p.Status,
                    OwnerId = p.OwnerId
                })
                .ToListAsync();

            if (projects.Count == 0)
            {
                throw new InvalidOperationException(requireManageRole
                    ? "Không tìm thấy project hoặc người dùng hiện tại không phải Owner/Admin."
                    : "Không tìm thấy project hoặc người dùng hiện tại không có quyền truy cập.");
            }

            if (projects.Count > 1)
            {
                throw new InvalidOperationException("Tên project không rõ ràng. Vui lòng cung cấp project code hoặc project id.");
            }

            return projects[0];
        }

        private static string GetDbSchema()
        {
            return """
            {
              "Users": ["Id", "Email", "FullName", "PhoneNumber", "AvatarUrl", "IsActive", "CreatedAt"],
              "Projects": ["Id", "Name", "Code", "Description", "OwnerId", "Status", "StartDate", "EndDate", "CreatedAt", "UpdatedAt"],
              "ProjectMembers": ["UserId", "ProjectId", "Role", "JoinedAt"],
              "Tasks": ["Id", "ProjectId", "Title", "Description", "Status", "Priority", "DueDate", "CreatedBy", "CreatedAt", "UpdatedAt", "StatusUpdatedAt", "IsDeleted"],
              "TaskAssignments": ["TaskId", "UserId", "AssignedAt"],
              "Notifications": ["Id", "UserId", "ProjectId", "TaskId", "TriggeredBy", "Title", "Message", "Type", "IsRead", "CreatedAt"]
            }
            """;
        }

        private static object BuildQueryPlanJsonSchema()
        {
            return new
            {
                type = "json_schema",
                name = "chatbot_query_plan",
                strict = true,
                schema = new
                {
                    type = "object",
                    additionalProperties = false,
                    required = new[]
                    {
                        "allowed",
                        "denyReason",
                        "needsClarification",
                        "clarificationQuestion",
                        "intent",
                        "riskLevel",
                        "parameters",
                        "answerPlan"
                    },
                    properties = new
                    {
                        allowed = new { type = "boolean" },
                        denyReason = new { type = new[] { "string", "null" } },
                        needsClarification = new { type = "boolean" },
                        clarificationQuestion = new { type = new[] { "string", "null" } },
                        intent = new
                        {
                            type = "string",
                            @enum = new[]
                            {
                                "my_dashboard_summary",
                                "count_my_overdue_tasks",
                                "list_my_tasks",
                                "list_my_created_tasks",
                                "list_my_notifications",
                                "list_my_projects",
                                "project_summary",
                                "list_project_tasks",
                                "list_project_members",
                                "managed_project_workload"
                            }
                        },
                        riskLevel = new { type = "string", @enum = new[] { "low", "medium", "high" } },
                        parameters = new
                        {
                            type = "object",
                            additionalProperties = false,
                            required = new[] { "projectId", "projectCode", "projectName", "status", "includeOverdueOnly", "limit" },
                            properties = new
                            {
                                projectId = new { type = new[] { "integer", "null" } },
                                projectCode = new { type = new[] { "string", "null" } },
                                projectName = new { type = new[] { "string", "null" } },
                                status = new { type = new[] { "string", "null" } },
                                includeOverdueOnly = new { type = "boolean" },
                                limit = new { type = "integer", minimum = 1, maximum = MaxLimit }
                            }
                        },
                        answerPlan = new { type = "string" }
                    }
                }
            };
        }

        public class ChatbotRequest
        {
            [Required, MaxLength(1000)]
            public string Question { get; set; } = string.Empty;
        }

        public class ChatbotResponse
        {
            public string Answer { get; set; } = string.Empty;
            public string Intent { get; set; } = string.Empty;
            public ChatbotQueryPlan? Plan { get; set; }
            public object? Data { get; set; }
        }

        public class ChatbotQueryPlan
        {
            public bool Allowed { get; set; }
            public string? DenyReason { get; set; }
            public bool NeedsClarification { get; set; }
            public string? ClarificationQuestion { get; set; }
            public string Intent { get; set; } = string.Empty;
            public string RiskLevel { get; set; } = "low";
            public ChatbotPlanParameters? Parameters { get; set; } = new();
            public string AnswerPlan { get; set; } = string.Empty;
        }

        public class ChatbotPlanParameters
        {
            public int? ProjectId { get; set; }
            public string? ProjectCode { get; set; }
            public string? ProjectName { get; set; }
            public string? Status { get; set; }
            public bool IncludeOverdueOnly { get; set; }
            public int Limit { get; set; } = 20;
        }

        public class ChatbotUserContext
        {
            public int CurrentUserId { get; set; }
            public string Email { get; set; } = string.Empty;
            public string FullName { get; set; } = string.Empty;
            public List<int> AccessibleProjectIds { get; set; } = [];
            public List<int> ManageableProjectIds { get; set; } = [];
            public Dictionary<string, string> RolesByProject { get; set; } = [];
        }

        public class ProjectLookupResult
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public string? Code { get; set; }
            public string Status { get; set; } = string.Empty;
            public int OwnerId { get; set; }
        }

        private sealed class PlanValidationResult
        {
            private PlanValidationResult(bool isValid, string? message)
            {
                IsValid = isValid;
                Message = message;
            }

            public bool IsValid { get; }
            public string? Message { get; }

            public static PlanValidationResult Valid() => new(true, null);
            public static PlanValidationResult Invalid(string message) => new(false, message);
        }
    }
}
