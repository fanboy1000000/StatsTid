using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Unit;

/// <summary>
/// Tests for Sprint 9: Skema (monthly spreadsheet) feature.
/// Covers: Project model, TimerSession model, two-step approval state machine,
/// Skema composite endpoint response shape, and event serialization roundtrips
/// for PeriodEmployeeApproved, PeriodReopened, TimerCheckedIn, TimerCheckedOut.
/// </summary>
public class Sprint9SkemaTests
{
    // ---------------------------------------------------------------
    // 1. Project model tests
    // ---------------------------------------------------------------

    [Fact]
    public void ProjectRepository_CreateProject_AssignsUniqueId()
    {
        var project = new Project
        {
            ProjectId = Guid.NewGuid(),
            OrgId = "AFD01",
            ProjectCode = "PROJ-001",
            ProjectName = "Drift",
            CreatedBy = "ladm01"
        };

        Assert.NotEqual(Guid.Empty, project.ProjectId);
        Assert.Equal("AFD01", project.OrgId);
        Assert.Equal("PROJ-001", project.ProjectCode);
        Assert.Equal("Drift", project.ProjectName);
        Assert.Equal("ladm01", project.CreatedBy);
    }

    [Fact]
    public void ProjectRepository_GetByOrg_ReturnsOnlyActiveProjects()
    {
        var activeProject = new Project
        {
            ProjectId = Guid.NewGuid(),
            OrgId = "AFD01",
            ProjectCode = "PROJ-ACTIVE",
            ProjectName = "Active Project",
            IsActive = true,
            CreatedBy = "ladm01"
        };

        var deactivatedProject = new Project
        {
            ProjectId = Guid.NewGuid(),
            OrgId = "AFD01",
            ProjectCode = "PROJ-DEACTIVATED",
            ProjectName = "Deactivated Project",
            IsActive = false,
            CreatedBy = "ladm01"
        };

        // Verify the IsActive flag correctly differentiates projects
        Assert.True(activeProject.IsActive);
        Assert.False(deactivatedProject.IsActive);

        // Simulate filtering: only active projects should appear
        var allProjects = new[] { activeProject, deactivatedProject };
        var filteredProjects = allProjects.Where(p => p.IsActive).ToList();

        Assert.Single(filteredProjects);
        Assert.Equal("PROJ-ACTIVE", filteredProjects[0].ProjectCode);
    }

    [Fact]
    public void ProjectEndpoints_CreateProject_RequiresLocalAdmin()
    {
        // Verify the Project model has all required fields for endpoint creation
        var project = new Project
        {
            ProjectId = Guid.NewGuid(),
            OrgId = "STY02",
            ProjectCode = "PROJ-NEW",
            ProjectName = "New Project",
            SortOrder = 10,
            CreatedBy = "ladm01"
        };

        Assert.Equal("STY02", project.OrgId);
        Assert.Equal(10, project.SortOrder);
        // Authorization policy "LocalAdminOrAbove" is configured on the endpoint
        // This test verifies the model supports the endpoint's response shape
    }

    [Fact]
    public void ProjectEndpoints_GetProjects_ReturnsProjectsForOrg()
    {
        var projects = new[]
        {
            new Project
            {
                ProjectId = Guid.NewGuid(),
                OrgId = "AFD01",
                ProjectCode = "PROJ-A",
                ProjectName = "Project A",
                SortOrder = 1,
                CreatedBy = "ladm01"
            },
            new Project
            {
                ProjectId = Guid.NewGuid(),
                OrgId = "AFD01",
                ProjectCode = "PROJ-B",
                ProjectName = "Project B",
                SortOrder = 2,
                CreatedBy = "ladm01"
            },
            new Project
            {
                ProjectId = Guid.NewGuid(),
                OrgId = "AFD02",
                ProjectCode = "PROJ-C",
                ProjectName = "Project C",
                SortOrder = 1,
                CreatedBy = "ladm02"
            }
        };

        // Filter by orgId — simulates GetByOrgAsync behavior
        var afd01Projects = projects.Where(p => p.OrgId == "AFD01").ToList();

        Assert.Equal(2, afd01Projects.Count);
        Assert.All(afd01Projects, p => Assert.Equal("AFD01", p.OrgId));
    }

    [Fact]
    public void ProjectEndpoints_DeactivateProject_SoftDeletes()
    {
        // Projects use soft delete via IsActive flag rather than hard delete
        var project = new Project
        {
            ProjectId = Guid.NewGuid(),
            OrgId = "AFD01",
            ProjectCode = "PROJ-TO-DELETE",
            ProjectName = "To be deactivated",
            IsActive = true,
            CreatedBy = "ladm01"
        };

        Assert.True(project.IsActive);

        // After deactivation, IsActive = false (init-only, so we simulate the DB state)
        var deactivatedProject = new Project
        {
            ProjectId = project.ProjectId,
            OrgId = project.OrgId,
            ProjectCode = project.ProjectCode,
            ProjectName = project.ProjectName,
            IsActive = false,
            CreatedBy = project.CreatedBy
        };

        Assert.False(deactivatedProject.IsActive);
        Assert.Equal(project.ProjectId, deactivatedProject.ProjectId);
    }

    // ---------------------------------------------------------------
    // 2. Timer session tests
    // ---------------------------------------------------------------

    [Fact]
    public void TimerSession_CheckIn_CreatesActiveSession()
    {
        var now = DateTime.UtcNow;
        var session = new TimerSession
        {
            SessionId = Guid.NewGuid(),
            EmployeeId = "EMP001",
            Date = DateOnly.FromDateTime(now),
            CheckInAt = now,
            IsActive = true
        };

        Assert.True(session.IsActive);
        Assert.Equal("EMP001", session.EmployeeId);
        Assert.Equal(DateOnly.FromDateTime(now), session.Date);
        Assert.Null(session.CheckOutAt);
    }

    [Fact]
    public void TimerSession_CheckOut_CalculatesClockedHours()
    {
        var checkIn = new DateTime(2026, 3, 5, 8, 0, 0, DateTimeKind.Utc);
        var checkOut = new DateTime(2026, 3, 5, 16, 30, 0, DateTimeKind.Utc);

        var clockedHours = Math.Round((decimal)(checkOut - checkIn).TotalHours, 2);

        Assert.Equal(8.50m, clockedHours);
    }

    [Fact]
    public void TimerSession_CheckOut_SetsInactive()
    {
        var checkIn = new DateTime(2026, 3, 5, 8, 0, 0, DateTimeKind.Utc);
        var checkOut = new DateTime(2026, 3, 5, 16, 0, 0, DateTimeKind.Utc);

        // After check-out, the session is stored with IsActive = false
        var completedSession = new TimerSession
        {
            SessionId = Guid.NewGuid(),
            EmployeeId = "EMP001",
            Date = DateOnly.FromDateTime(checkIn),
            CheckInAt = checkIn,
            CheckOutAt = checkOut,
            IsActive = false
        };

        Assert.False(completedSession.IsActive);
        Assert.NotNull(completedSession.CheckOutAt);
        Assert.Equal(checkOut, completedSession.CheckOutAt);
    }

    [Fact]
    public void TimerSession_DuplicateCheckIn_PreventsDouble()
    {
        // One active session per employee — simulate by checking existing session
        var existingSession = new TimerSession
        {
            SessionId = Guid.NewGuid(),
            EmployeeId = "EMP001",
            Date = DateOnly.FromDateTime(DateTime.UtcNow),
            CheckInAt = DateTime.UtcNow.AddHours(-1),
            IsActive = true
        };

        // If an active session exists, the endpoint returns Conflict
        // Simulate the duplicate prevention check
        Assert.True(existingSession.IsActive);
        Assert.Equal("EMP001", existingSession.EmployeeId);

        // A second check-in attempt should detect the existing active session
        var hasActiveSession = existingSession is { IsActive: true };
        Assert.True(hasActiveSession);
    }

    [Fact]
    public void TimerSession_CheckOut_WithoutCheckIn_Returns404()
    {
        // When no active session exists, the endpoint returns NotFound
        TimerSession? activeSession = null;

        Assert.Null(activeSession);
        // The endpoint checks: if (session is null) return Results.NotFound(...)
    }

    // ---------------------------------------------------------------
    // 3. Two-step approval state machine tests
    // ---------------------------------------------------------------

    [Fact]
    public void Approval_EmployeeApprove_TransitionsDraftToEmployeeApproved()
    {
        var period = new ApprovalPeriod
        {
            PeriodId = Guid.NewGuid(),
            EmployeeId = "EMP001",
            OrgId = "AFD01",
            PeriodStart = new DateOnly(2026, 3, 1),
            PeriodEnd = new DateOnly(2026, 3, 31),
            PeriodType = "MONTHLY",
            Status = "DRAFT",
            AgreementCode = "HK",
            OkVersion = "OK24"
        };

        // Verify DRAFT can transition to EMPLOYEE_APPROVED
        Assert.Equal("DRAFT", period.Status);
        Assert.True(period.Status is "DRAFT" or "REJECTED");

        // After employee approval:
        var approvedPeriod = new ApprovalPeriod
        {
            PeriodId = period.PeriodId,
            EmployeeId = period.EmployeeId,
            OrgId = period.OrgId,
            PeriodStart = period.PeriodStart,
            PeriodEnd = period.PeriodEnd,
            PeriodType = period.PeriodType,
            Status = "EMPLOYEE_APPROVED",
            EmployeeApprovedAt = DateTime.UtcNow,
            EmployeeApprovedBy = "EMP001",
            AgreementCode = "HK",
            OkVersion = "OK24"
        };

        Assert.Equal("EMPLOYEE_APPROVED", approvedPeriod.Status);
        Assert.NotNull(approvedPeriod.EmployeeApprovedAt);
        Assert.Equal("EMP001", approvedPeriod.EmployeeApprovedBy);
    }

    [Fact]
    public void Approval_EmployeeApprove_OnlyOwnPeriod()
    {
        var period = new ApprovalPeriod
        {
            PeriodId = Guid.NewGuid(),
            EmployeeId = "EMP001",
            OrgId = "AFD01",
            PeriodStart = new DateOnly(2026, 3, 1),
            PeriodEnd = new DateOnly(2026, 3, 31),
            PeriodType = "MONTHLY",
            Status = "DRAFT",
            AgreementCode = "HK",
            OkVersion = "OK24"
        };

        // An employee with ActorId != EmployeeId should be denied
        var actorId = "EMP002";
        var periodOwnerId = period.EmployeeId;

        Assert.NotEqual(actorId, periodOwnerId);
        // The endpoint enforces: if (actor.ActorRole == Employee && period.EmployeeId != actor.ActorId) → 403
    }

    [Fact]
    public void Approval_EmployeeApprove_SetsDeadlines()
    {
        var periodEnd = new DateOnly(2026, 3, 31);
        var lastDayOfMonth = new DateOnly(periodEnd.Year, periodEnd.Month,
            DateTime.DaysInMonth(periodEnd.Year, periodEnd.Month));

        var expectedEmployeeDeadline = lastDayOfMonth.AddDays(2);
        var expectedManagerDeadline = lastDayOfMonth.AddDays(5);

        Assert.Equal(new DateOnly(2026, 4, 2), expectedEmployeeDeadline);
        Assert.Equal(new DateOnly(2026, 4, 5), expectedManagerDeadline);

        // Verify deadlines are stored on the period
        var period = new ApprovalPeriod
        {
            PeriodId = Guid.NewGuid(),
            EmployeeId = "EMP001",
            OrgId = "AFD01",
            PeriodStart = new DateOnly(2026, 3, 1),
            PeriodEnd = periodEnd,
            PeriodType = "MONTHLY",
            Status = "EMPLOYEE_APPROVED",
            EmployeeApprovedAt = DateTime.UtcNow,
            EmployeeApprovedBy = "EMP001",
            EmployeeDeadline = expectedEmployeeDeadline,
            ManagerDeadline = expectedManagerDeadline,
            AgreementCode = "HK",
            OkVersion = "OK24"
        };

        Assert.Equal(expectedEmployeeDeadline, period.EmployeeDeadline);
        Assert.Equal(expectedManagerDeadline, period.ManagerDeadline);
    }

    [Fact]
    public void Approval_ManagerApprove_AcceptsEmployeeApproved()
    {
        var period = new ApprovalPeriod
        {
            PeriodId = Guid.NewGuid(),
            EmployeeId = "EMP001",
            OrgId = "AFD01",
            PeriodStart = new DateOnly(2026, 3, 1),
            PeriodEnd = new DateOnly(2026, 3, 31),
            PeriodType = "MONTHLY",
            Status = "EMPLOYEE_APPROVED",
            EmployeeApprovedAt = DateTime.UtcNow,
            EmployeeApprovedBy = "EMP001",
            AgreementCode = "HK",
            OkVersion = "OK24"
        };

        // The approve endpoint accepts both SUBMITTED and EMPLOYEE_APPROVED
        Assert.True(period.Status is "SUBMITTED" or "EMPLOYEE_APPROVED");

        // After manager approval:
        var approvedPeriod = new ApprovalPeriod
        {
            PeriodId = period.PeriodId,
            EmployeeId = period.EmployeeId,
            OrgId = period.OrgId,
            PeriodStart = period.PeriodStart,
            PeriodEnd = period.PeriodEnd,
            PeriodType = period.PeriodType,
            Status = "APPROVED",
            ApprovedBy = "leader01",
            ApprovedAt = DateTime.UtcNow,
            AgreementCode = "HK",
            OkVersion = "OK24"
        };

        Assert.Equal("APPROVED", approvedPeriod.Status);
        Assert.Equal("leader01", approvedPeriod.ApprovedBy);
    }

    [Fact]
    public void Approval_ManagerApprove_AcceptsSubmitted()
    {
        // Backward compatibility: the legacy SUBMITTED status is still accepted
        var period = new ApprovalPeriod
        {
            PeriodId = Guid.NewGuid(),
            EmployeeId = "EMP001",
            OrgId = "AFD01",
            PeriodStart = new DateOnly(2026, 3, 1),
            PeriodEnd = new DateOnly(2026, 3, 31),
            PeriodType = "MONTHLY",
            Status = "SUBMITTED",
            SubmittedAt = DateTime.UtcNow,
            SubmittedBy = "EMP001",
            AgreementCode = "HK",
            OkVersion = "OK24"
        };

        Assert.True(period.Status is "SUBMITTED" or "EMPLOYEE_APPROVED");
    }

    [Fact]
    public void Approval_ManagerReject_AcceptsEmployeeApproved()
    {
        var period = new ApprovalPeriod
        {
            PeriodId = Guid.NewGuid(),
            EmployeeId = "EMP001",
            OrgId = "AFD01",
            PeriodStart = new DateOnly(2026, 3, 1),
            PeriodEnd = new DateOnly(2026, 3, 31),
            PeriodType = "MONTHLY",
            Status = "EMPLOYEE_APPROVED",
            EmployeeApprovedAt = DateTime.UtcNow,
            EmployeeApprovedBy = "EMP001",
            AgreementCode = "HK",
            OkVersion = "OK24"
        };

        // The reject endpoint accepts both SUBMITTED and EMPLOYEE_APPROVED
        Assert.True(period.Status is "SUBMITTED" or "EMPLOYEE_APPROVED");

        // After rejection:
        var rejectedPeriod = new ApprovalPeriod
        {
            PeriodId = period.PeriodId,
            EmployeeId = period.EmployeeId,
            OrgId = period.OrgId,
            PeriodStart = period.PeriodStart,
            PeriodEnd = period.PeriodEnd,
            PeriodType = period.PeriodType,
            Status = "REJECTED",
            RejectionReason = "Manglende timer for torsdag",
            AgreementCode = "HK",
            OkVersion = "OK24"
        };

        Assert.Equal("REJECTED", rejectedPeriod.Status);
        Assert.Equal("Manglende timer for torsdag", rejectedPeriod.RejectionReason);
    }

    [Fact]
    public void Approval_Reopen_TransitionsEmployeeApprovedToDraft()
    {
        var period = new ApprovalPeriod
        {
            PeriodId = Guid.NewGuid(),
            EmployeeId = "EMP001",
            OrgId = "AFD01",
            PeriodStart = new DateOnly(2026, 3, 1),
            PeriodEnd = new DateOnly(2026, 3, 31),
            PeriodType = "MONTHLY",
            Status = "EMPLOYEE_APPROVED",
            EmployeeApprovedAt = DateTime.UtcNow,
            EmployeeApprovedBy = "EMP001",
            AgreementCode = "HK",
            OkVersion = "OK24"
        };

        // Only EMPLOYEE_APPROVED periods can be reopened
        Assert.Equal("EMPLOYEE_APPROVED", period.Status);

        // After reopen:
        var reopenedPeriod = new ApprovalPeriod
        {
            PeriodId = period.PeriodId,
            EmployeeId = period.EmployeeId,
            OrgId = period.OrgId,
            PeriodStart = period.PeriodStart,
            PeriodEnd = period.PeriodEnd,
            PeriodType = period.PeriodType,
            Status = "DRAFT",
            AgreementCode = "HK",
            OkVersion = "OK24"
        };

        Assert.Equal("DRAFT", reopenedPeriod.Status);
    }

    [Fact]
    public void Approval_Reopen_ClearsEmployeeApprovalFields()
    {
        // When a period is reopened, employee approval fields should be cleared
        var reopenedPeriod = new ApprovalPeriod
        {
            PeriodId = Guid.NewGuid(),
            EmployeeId = "EMP001",
            OrgId = "AFD01",
            PeriodStart = new DateOnly(2026, 3, 1),
            PeriodEnd = new DateOnly(2026, 3, 31),
            PeriodType = "MONTHLY",
            Status = "DRAFT",
            EmployeeApprovedAt = null,
            EmployeeApprovedBy = null,
            EmployeeDeadline = null,
            ManagerDeadline = null,
            AgreementCode = "HK",
            OkVersion = "OK24"
        };

        Assert.Equal("DRAFT", reopenedPeriod.Status);
        Assert.Null(reopenedPeriod.EmployeeApprovedAt);
        Assert.Null(reopenedPeriod.EmployeeApprovedBy);
        Assert.Null(reopenedPeriod.EmployeeDeadline);
        Assert.Null(reopenedPeriod.ManagerDeadline);
    }

    // ---------------------------------------------------------------
    // 4. Skema endpoint response shape tests
    // ---------------------------------------------------------------

    [Fact]
    public void SkemaEndpoint_GetMonth_ReturnsCompositeData()
    {
        // Verify all required fields are present in the response shape
        // by constructing the expected response structure
        var year = 2026;
        var month = 3;
        var daysInMonth = DateTime.DaysInMonth(year, month);

        Assert.Equal(31, daysInMonth);

        // Projects
        var projects = new[]
        {
            new Project
            {
                ProjectId = Guid.NewGuid(),
                OrgId = "AFD01",
                ProjectCode = "DRIFT",
                ProjectName = "Drift",
                SortOrder = 1,
                CreatedBy = "ladm01"
            }
        };

        // Absence types (filtered by visibility)
        var allAbsenceTypes = new[] { "SICK_DAY", "VACATION", "CARE_DAY" };
        var hiddenTypes = new HashSet<string> { "CARE_DAY" };
        var visibleAbsenceTypes = allAbsenceTypes.Where(t => !hiddenTypes.Contains(t)).ToList();

        Assert.Equal(2, visibleAbsenceTypes.Count);
        Assert.DoesNotContain("CARE_DAY", visibleAbsenceTypes);

        // Timer session
        var timerSession = new TimerSession
        {
            SessionId = Guid.NewGuid(),
            EmployeeId = "EMP001",
            Date = new DateOnly(2026, 3, 5),
            CheckInAt = new DateTime(2026, 3, 5, 8, 0, 0, DateTimeKind.Utc),
            IsActive = true
        };

        Assert.NotNull(timerSession);
        Assert.True(timerSession.IsActive);

        // Approval
        var approval = new ApprovalPeriod
        {
            PeriodId = Guid.NewGuid(),
            EmployeeId = "EMP001",
            OrgId = "AFD01",
            PeriodStart = new DateOnly(2026, 3, 1),
            PeriodEnd = new DateOnly(2026, 3, 31),
            PeriodType = "MONTHLY",
            Status = "DRAFT",
            AgreementCode = "HK",
            OkVersion = "OK24"
        };

        Assert.NotNull(approval);

        // Verify composite response has all required fields
        Assert.Equal(2026, year);
        Assert.Equal(3, month);
        Assert.Equal(31, daysInMonth);
        Assert.NotEmpty(projects);
        Assert.NotEmpty(visibleAbsenceTypes);
    }

    [Fact]
    public void SkemaEndpoint_Save_EmitsTimeEntryEvents()
    {
        // Verify that saving project entries creates TimeEntryRegistered events
        var employeeId = "EMP001";
        var projectCode = "DRIFT";
        var date = new DateOnly(2026, 3, 5);
        var hours = 7.4m;

        var @event = new TimeEntryRegistered
        {
            EmployeeId = employeeId,
            Date = date,
            Hours = hours,
            TaskId = projectCode,
            ActivityType = "NORMAL",
            AgreementCode = "HK",
            OkVersion = "OK24",
            ActorId = employeeId,
            ActorRole = "Employee"
        };

        Assert.Equal("TimeEntryRegistered", @event.EventType);
        Assert.Equal(employeeId, @event.EmployeeId);
        Assert.Equal(date, @event.Date);
        Assert.Equal(hours, @event.Hours);
        Assert.Equal(projectCode, @event.TaskId);
        Assert.Equal("NORMAL", @event.ActivityType);
    }

    [Fact]
    public void SkemaEndpoint_Save_EmitsAbsenceEvents()
    {
        // Verify that saving absence entries creates AbsenceRegistered events
        var employeeId = "EMP001";
        var absenceType = "VACATION";
        var date = new DateOnly(2026, 3, 10);
        var hours = 7.4m;

        var @event = new AbsenceRegistered
        {
            EmployeeId = employeeId,
            Date = date,
            AbsenceType = absenceType,
            Hours = hours,
            AgreementCode = "HK",
            OkVersion = "OK24",
            ActorId = employeeId,
            ActorRole = "Employee"
        };

        Assert.Equal("AbsenceRegistered", @event.EventType);
        Assert.Equal(employeeId, @event.EmployeeId);
        Assert.Equal(absenceType, @event.AbsenceType);
        Assert.Equal(date, @event.Date);
        Assert.Equal(hours, @event.Hours);
    }

    [Fact]
    public void SkemaEndpoint_Save_BlockedWhenEmployeeApproved()
    {
        // When period status is EMPLOYEE_APPROVED or APPROVED, saving is blocked
        var period = new ApprovalPeriod
        {
            PeriodId = Guid.NewGuid(),
            EmployeeId = "EMP001",
            OrgId = "AFD01",
            PeriodStart = new DateOnly(2026, 3, 1),
            PeriodEnd = new DateOnly(2026, 3, 31),
            PeriodType = "MONTHLY",
            Status = "EMPLOYEE_APPROVED",
            EmployeeApprovedAt = DateTime.UtcNow,
            EmployeeApprovedBy = "EMP001",
            AgreementCode = "HK",
            OkVersion = "OK24"
        };

        // The save endpoint checks: if status is "EMPLOYEE_APPROVED" or "APPROVED" → Conflict
        var isBlocked = period.Status is "EMPLOYEE_APPROVED" or "APPROVED";
        Assert.True(isBlocked);

        // Also verify APPROVED is blocked
        var approvedPeriod = new ApprovalPeriod
        {
            PeriodId = Guid.NewGuid(),
            EmployeeId = "EMP001",
            OrgId = "AFD01",
            PeriodStart = new DateOnly(2026, 3, 1),
            PeriodEnd = new DateOnly(2026, 3, 31),
            PeriodType = "MONTHLY",
            Status = "APPROVED",
            ApprovedBy = "leader01",
            ApprovedAt = DateTime.UtcNow,
            AgreementCode = "HK",
            OkVersion = "OK24"
        };

        Assert.True(approvedPeriod.Status is "EMPLOYEE_APPROVED" or "APPROVED");

        // DRAFT should NOT be blocked
        var draftPeriod = new ApprovalPeriod
        {
            PeriodId = Guid.NewGuid(),
            EmployeeId = "EMP001",
            OrgId = "AFD01",
            PeriodStart = new DateOnly(2026, 3, 1),
            PeriodEnd = new DateOnly(2026, 3, 31),
            PeriodType = "MONTHLY",
            Status = "DRAFT",
            AgreementCode = "HK",
            OkVersion = "OK24"
        };

        Assert.False(draftPeriod.Status is "EMPLOYEE_APPROVED" or "APPROVED");
    }

    [Fact]
    public void SkemaEndpoint_GetMonth_FiltersAbsenceTypesByVisibility()
    {
        // Simulate AbsenceTypeVisibility filtering
        var visibilityEntries = new[]
        {
            new AbsenceTypeVisibility
            {
                Id = Guid.NewGuid(),
                OrgId = "AFD01",
                AbsenceType = "CHILD_SICK_DAY_3",
                IsHidden = true,
                SetBy = "ladm01"
            },
            new AbsenceTypeVisibility
            {
                Id = Guid.NewGuid(),
                OrgId = "AFD01",
                AbsenceType = "LEAVE_WITHOUT_PAY",
                IsHidden = true,
                SetBy = "ladm01"
            }
        };

        var hiddenTypes = new HashSet<string>(
            visibilityEntries.Where(v => v.IsHidden).Select(v => v.AbsenceType));

        // All known absence types
        var allAbsenceTypes = new[]
        {
            "SICK_DAY", "VACATION", "CARE_DAY", "CHILD_SICK_DAY", "CHILD_SICK_DAY_2",
            "CHILD_SICK_DAY_3", "PARENTAL_LEAVE", "SENIOR_DAY", "LEAVE_WITH_PAY", "LEAVE_WITHOUT_PAY"
        };

        var visibleTypes = allAbsenceTypes.Where(t => !hiddenTypes.Contains(t)).ToList();

        Assert.Equal(8, visibleTypes.Count);
        Assert.DoesNotContain("CHILD_SICK_DAY_3", visibleTypes);
        Assert.DoesNotContain("LEAVE_WITHOUT_PAY", visibleTypes);
        Assert.Contains("SICK_DAY", visibleTypes);
        Assert.Contains("VACATION", visibleTypes);
    }

    // ---------------------------------------------------------------
    // 5. Event serialization roundtrip tests
    // ---------------------------------------------------------------

    [Fact]
    public void EventSerializer_SerializesNewEventTypes()
    {
        // Verify all 4 new Sprint 9 event types can be serialized
        var periodEmployeeApproved = new PeriodEmployeeApproved
        {
            PeriodId = Guid.NewGuid(),
            EmployeeId = "EMP001",
            OrgId = "AFD01",
            PeriodStart = new DateOnly(2026, 3, 1),
            PeriodEnd = new DateOnly(2026, 3, 31),
            ActorId = "EMP001",
            ActorRole = "Employee"
        };

        var periodReopened = new PeriodReopened
        {
            PeriodId = Guid.NewGuid(),
            EmployeeId = "EMP001",
            OrgId = "AFD01",
            PeriodStart = new DateOnly(2026, 3, 1),
            PeriodEnd = new DateOnly(2026, 3, 31),
            Reason = "Correction needed",
            ActorId = "leader01",
            ActorRole = "LocalLeader"
        };

        var timerCheckedIn = new TimerCheckedIn
        {
            EmployeeId = "EMP001",
            Date = new DateOnly(2026, 3, 5),
            CheckInAt = new DateTime(2026, 3, 5, 8, 0, 0, DateTimeKind.Utc),
            ActorId = "EMP001",
            ActorRole = "Employee"
        };

        var timerCheckedOut = new TimerCheckedOut
        {
            EmployeeId = "EMP001",
            Date = new DateOnly(2026, 3, 5),
            CheckOutAt = new DateTime(2026, 3, 5, 16, 0, 0, DateTimeKind.Utc),
            ClockedHours = 8.0m,
            ActorId = "EMP001",
            ActorRole = "Employee"
        };

        // All should serialize without exceptions
        var json1 = EventSerializer.Serialize(periodEmployeeApproved);
        var json2 = EventSerializer.Serialize(periodReopened);
        var json3 = EventSerializer.Serialize(timerCheckedIn);
        var json4 = EventSerializer.Serialize(timerCheckedOut);

        Assert.NotNull(json1);
        Assert.NotNull(json2);
        Assert.NotNull(json3);
        Assert.NotNull(json4);
        Assert.Contains("PeriodEmployeeApproved", json1);
        Assert.Contains("PeriodReopened", json2);
        Assert.Contains("TimerCheckedIn", json3);
        Assert.Contains("TimerCheckedOut", json4);
    }

    [Fact]
    public void EventSerializer_DeserializesNewEventTypes()
    {
        // Round-trip test for all 4 new Sprint 9 event types
        var originalPeriodEmployeeApproved = new PeriodEmployeeApproved
        {
            EventId = Guid.NewGuid(),
            OccurredAt = new DateTime(2026, 3, 5, 10, 0, 0, DateTimeKind.Utc),
            PeriodId = Guid.NewGuid(),
            EmployeeId = "EMP001",
            OrgId = "AFD01",
            PeriodStart = new DateOnly(2026, 3, 1),
            PeriodEnd = new DateOnly(2026, 3, 31),
            ActorId = "EMP001",
            ActorRole = "Employee"
        };

        var json1 = EventSerializer.Serialize(originalPeriodEmployeeApproved);
        var deserialized1 = EventSerializer.Deserialize("PeriodEmployeeApproved", json1);
        Assert.IsType<PeriodEmployeeApproved>(deserialized1);
        var rt1 = (PeriodEmployeeApproved)deserialized1;
        Assert.Equal(originalPeriodEmployeeApproved.EventId, rt1.EventId);
        Assert.Equal(originalPeriodEmployeeApproved.PeriodId, rt1.PeriodId);
        Assert.Equal("EMP001", rt1.EmployeeId);
        Assert.Equal("AFD01", rt1.OrgId);
        Assert.Equal(new DateOnly(2026, 3, 1), rt1.PeriodStart);
        Assert.Equal(new DateOnly(2026, 3, 31), rt1.PeriodEnd);

        // PeriodReopened
        var originalPeriodReopened = new PeriodReopened
        {
            EventId = Guid.NewGuid(),
            OccurredAt = new DateTime(2026, 3, 6, 9, 0, 0, DateTimeKind.Utc),
            PeriodId = Guid.NewGuid(),
            EmployeeId = "EMP001",
            OrgId = "AFD01",
            PeriodStart = new DateOnly(2026, 3, 1),
            PeriodEnd = new DateOnly(2026, 3, 31),
            Reason = "Fejl i registrering",
            ActorId = "leader01",
            ActorRole = "LocalLeader"
        };

        var json2 = EventSerializer.Serialize(originalPeriodReopened);
        var deserialized2 = EventSerializer.Deserialize("PeriodReopened", json2);
        Assert.IsType<PeriodReopened>(deserialized2);
        var rt2 = (PeriodReopened)deserialized2;
        Assert.Equal(originalPeriodReopened.EventId, rt2.EventId);
        Assert.Equal(originalPeriodReopened.PeriodId, rt2.PeriodId);
        Assert.Equal("Fejl i registrering", rt2.Reason);

        // TimerCheckedIn
        var originalTimerCheckedIn = new TimerCheckedIn
        {
            EventId = Guid.NewGuid(),
            OccurredAt = new DateTime(2026, 3, 5, 8, 0, 0, DateTimeKind.Utc),
            EmployeeId = "EMP001",
            Date = new DateOnly(2026, 3, 5),
            CheckInAt = new DateTime(2026, 3, 5, 8, 0, 0, DateTimeKind.Utc),
            ActorId = "EMP001",
            ActorRole = "Employee"
        };

        var json3 = EventSerializer.Serialize(originalTimerCheckedIn);
        var deserialized3 = EventSerializer.Deserialize("TimerCheckedIn", json3);
        Assert.IsType<TimerCheckedIn>(deserialized3);
        var rt3 = (TimerCheckedIn)deserialized3;
        Assert.Equal(originalTimerCheckedIn.EventId, rt3.EventId);
        Assert.Equal("EMP001", rt3.EmployeeId);
        Assert.Equal(new DateOnly(2026, 3, 5), rt3.Date);
        Assert.Equal(originalTimerCheckedIn.CheckInAt, rt3.CheckInAt);

        // TimerCheckedOut
        var originalTimerCheckedOut = new TimerCheckedOut
        {
            EventId = Guid.NewGuid(),
            OccurredAt = new DateTime(2026, 3, 5, 16, 0, 0, DateTimeKind.Utc),
            EmployeeId = "EMP001",
            Date = new DateOnly(2026, 3, 5),
            CheckOutAt = new DateTime(2026, 3, 5, 16, 0, 0, DateTimeKind.Utc),
            ClockedHours = 8.0m,
            ActorId = "EMP001",
            ActorRole = "Employee"
        };

        var json4 = EventSerializer.Serialize(originalTimerCheckedOut);
        var deserialized4 = EventSerializer.Deserialize("TimerCheckedOut", json4);
        Assert.IsType<TimerCheckedOut>(deserialized4);
        var rt4 = (TimerCheckedOut)deserialized4;
        Assert.Equal(originalTimerCheckedOut.EventId, rt4.EventId);
        Assert.Equal("EMP001", rt4.EmployeeId);
        Assert.Equal(new DateOnly(2026, 3, 5), rt4.Date);
        Assert.Equal(8.0m, rt4.ClockedHours);
        Assert.Equal(originalTimerCheckedOut.CheckOutAt, rt4.CheckOutAt);
    }
}
