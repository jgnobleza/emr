namespace medrec.Models;

public sealed class Patient
{
    public int Id { get; set; }
    public string ClientUid { get; set; } = string.Empty;
    public string PatientNumber { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public int Age { get; set; }
    public string Address { get; set; } = string.Empty;
    public string Sex { get; set; } = string.Empty;
    public string CivilStatus { get; set; } = string.Empty;
    public string ContactNumber { get; set; } = string.Empty;
    public string Occupation { get; set; } = string.Empty;
    public string Company { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PartnerName { get; set; } = string.Empty;
    public string PartnerContactNumber { get; set; } = string.Empty;
    public string ReferredBy { get; set; } = string.Empty;
    public int? AgeOfMenarche { get; set; }
    public int? MenopauseAge { get; set; }
    public DateOnly? PreviousMenstrualPeriod { get; set; }
    public int? PeriodCycleDays { get; set; }
    public int? PeriodDurationDays { get; set; }
    public string MenstrualAmount { get; set; } = string.Empty;
    public string MenstrualPattern { get; set; } = string.Empty;
    public bool? SexuallyActive { get; set; }
    public string ContraceptionMethod { get; set; } = string.Empty;
    public decimal? HeightCm { get; set; }
    public decimal? WeightKg { get; set; }
    public string BloodPressure { get; set; } = string.Empty;
    public string FetalHeartTone { get; set; } = string.Empty;
    public DateOnly? LastMenstrualPeriod { get; set; }
    public string? PhotoUrl { get; set; }
    public string LastCheckupComplaint { get; set; } = string.Empty;
    public DateTime LastUpdatedAt { get; set; }
    public DateTime? ArchivedAt { get; set; }
    public string SyncStatus { get; set; } = "Synced";

    public decimal? Bmi
    {
        get
        {
            if (!HeightCm.HasValue || !WeightKg.HasValue || HeightCm <= 0)
            {
                return null;
            }

            var heightMeters = HeightCm.Value / 100m;
            return Math.Round(WeightKg.Value / (heightMeters * heightMeters), 1);
        }
    }

    public string BmiLabel => Bmi.HasValue ? Bmi.Value.ToString("0.0") : "-";

    public DateOnly? EstimatedDueDate => LastMenstrualPeriod?.AddDays(280);

    public string EstimatedDueDateLabel => EstimatedDueDate.HasValue
        ? EstimatedDueDate.Value < DateOnly.FromDateTime(DateTime.Today)
            ? "Past due"
            : EstimatedDueDate.Value == DateOnly.FromDateTime(DateTime.Today)
                ? "Due today"
                : EstimatedDueDate.Value.ToString("MMM d, yyyy")
        : "-";

    public string AgeOfGestationLabel
    {
        get
        {
            if (!LastMenstrualPeriod.HasValue)
            {
                return "-";
            }

            var days = DateOnly.FromDateTime(DateTime.Today).DayNumber - LastMenstrualPeriod.Value.DayNumber;
            if (days < 0)
            {
                return "-";
            }

            if (days >= 42 * 7)
            {
                return "Post-term";
            }

            return $"{days / 7}w {days % 7}d";
        }
    }

    public string SexuallyActiveLabel => SexuallyActive.HasValue
        ? SexuallyActive.Value ? "Yes" : "No"
        : "-";

    public string LastCheckupComplaintLabel => string.IsNullOrWhiteSpace(LastCheckupComplaint)
        ? "-"
        : LastCheckupComplaint;
}
