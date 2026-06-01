using System.ComponentModel.DataAnnotations;

namespace Projex_backend.Dtos
{

    public class PostProject
    {

    }
    public class PaginationQuery
    {
        [Range(1, int.MaxValue)]
        public int Page { get; set; } = 1;

        [Range(1, 100)]
        public int PageSize { get; set; } = 10;
    }

    public class RegisterRequest
    {
        [Required, EmailAddress, MaxLength(255)]
        public string Email { get; set; } = string.Empty;

        [Required, MinLength(6), MaxLength(100)]
        public string Password { get; set; } = string.Empty;

        [Required, MaxLength(255)]
        public string FullName { get; set; } = string.Empty;
    }

    public class LoginRequest
    {
        [Required, EmailAddress, MaxLength(255)]
        public string Email { get; set; } = string.Empty;

        [Required, MinLength(6), MaxLength(100)]
        public string Password { get; set; } = string.Empty;
    }

    public class ChangePasswordRequest
    {
        [Required]
        public string CurrentPassword { get; set; } = string.Empty;

        [Required, MinLength(6), MaxLength(100)]
        public string NewPassword { get; set; } = string.Empty;
    }

    public class UpdateProfileRequest
    {
        [Required, EmailAddress, MaxLength(255)]
        public string Email { get; set; } = string.Empty;

        [Required, MaxLength(255)]
        public string FullName { get; set; } = string.Empty;

        [MaxLength(20)]
        public string? PhoneNumber { get; set; }
    }

    public class UpdateAvatarRequest
    {
        [Required, MaxLength(500)]
        public string AvatarUrl { get; set; } = string.Empty;
    }

    public class ForgotPasswordRequest
    {
        [Required, EmailAddress, MaxLength(255)]
        public string Email { get; set; } = string.Empty;
    }

    public class VerifyResetCodeRequest
    {
        [Required, EmailAddress, MaxLength(255)]
        public string Email { get; set; } = string.Empty;

        [Required, RegularExpression(@"^\d{4}$")]
        public string Code { get; set; } = string.Empty;
    }

    public class ResetPasswordRequest
    {
        [Required, EmailAddress, MaxLength(255)]
        public string Email { get; set; } = string.Empty;

        [Required, RegularExpression(@"^\d{4}$")]
        public string Code { get; set; } = string.Empty;

        [Required, MinLength(6), MaxLength(100)]
        public string NewPassword { get; set; } = string.Empty;
    }

    public class ProjectQuery : PaginationQuery
    {
        public string? Keyword { get; set; }
        public string? Status { get; set; }
        public string? SortBy { get; set; }
        public string? SortOrder { get; set; }
    }

    public class CreateProjectRequest
    {
        [Required, MaxLength(255)]
        public string Name { get; set; } = string.Empty;

        [Required, MaxLength(50)]
        public string Code { get; set; } = string.Empty;

        public string? Description { get; set; }

        public DateTime? StartDate { get; set; }

        public DateTime? EndDate { get; set; }
    }

    public class UpdateProjectRequest : CreateProjectRequest
    {
        [Required, MaxLength(50)]
        public string Status { get; set; } = "Active";
    }

    public class UpdateProjectStatusRequest
    {
        [Required, MaxLength(50)]
        public string Status { get; set; } = "Active";
    }

    public class MemberQuery : PaginationQuery
    {
        public string? Keyword { get; set; }
        public string? Role { get; set; }
    }

    public class AddProjectMemberRequest
    {
        [Range(1, int.MaxValue)]
        public int UserId { get; set; }

        [Required, MaxLength(50)]
        public string Role { get; set; } = "Member";
    }

    public class AddProjectMemberByEmailRequest
    {
        [Required, MaxLength(255)]
        public string Email { get; set; }

        [Required, MaxLength(50)]
        public string Role { get; set; } = "Member";
    }

    public class UpdateProjectMemberRoleRequest
    {
        [Required, MaxLength(50)]
        public string Role { get; set; } = "Member";
    }

    public class TaskQuery : PaginationQuery
    {
        public string? Keyword { get; set; }
        public string? Status { get; set; }
        [Range(1, 3)]
        public int? Priority { get; set; }
        public int? AssignedUserId { get; set; }
        public int? CreatedBy { get; set; }
        public DateTime? DueFrom { get; set; }
        public DateTime? DueTo { get; set; }
        public bool? IsOverdue { get; set; }
        public string? SortBy { get; set; }
        public string? SortOrder { get; set; }
    }

    public class CreateTaskRequest
    {
        [Required, MaxLength(255)]
        public string Title { get; set; } = string.Empty;

        public string? Description { get; set; }

        [MaxLength(50)]
        public string Status { get; set; } = "Assigned";

        [Range(1, 3)]
        public int Priority { get; set; } = 2;

        public DateTime? DueDate { get; set; }

        public List<int> AssignedUserIds { get; set; } = [];
    }

    public class UpdateTaskRequest : CreateTaskRequest
    {
    }

    public class UpdateTaskStatusRequest
    {
        [Required, MaxLength(50)]
        public string Status { get; set; } = string.Empty;
    }

    public class AssignUsersRequest
    {
        [Required]
        public List<int> UserIds { get; set; } = [];
    }

    public class CreateAttachmentRequest
    {
        [Required, MaxLength(500)]
        public string FileUrl { get; set; } = string.Empty;
    }

    public class UserSearchQuery : PaginationQuery
    {
        public string? Keyword { get; set; }
    }

    public class NotificationQuery : PaginationQuery
    {
        public bool? IsRead { get; set; }
    }
}
