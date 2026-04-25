using Microsoft.Data.SqlClient;
using System.Data;
using Tutorial7.DTOs;

namespace Tutorial7.Services;

public class AppointmentsService : IAppointmentsService
{
    private readonly string _connectionString;

    public AppointmentsService(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")!;
    }
    

    public async Task<IEnumerable<AppointmentListDto>> GetAllAppointmentsAsync(
        string? status, string? patientLastName)
    {
        const string sql = """
            SELECT
                a.IdAppointment,
                a.AppointmentDate,
                a.Status,
                a.Reason,
                p.FirstName + N' ' + p.LastName AS PatientFullName,
                p.Email AS PatientEmail
            FROM dbo.Appointments a
            JOIN dbo.Patients p ON p.IdPatient = a.IdPatient
            WHERE (@Status IS NULL OR a.Status = @Status)
              AND (@PatientLastName IS NULL OR p.LastName = @PatientLastName)
            ORDER BY a.AppointmentDate;
            """;

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@Status", SqlDbType.NVarChar, 50).Value =
            (object?)status ?? DBNull.Value;
        command.Parameters.Add("@PatientLastName", SqlDbType.NVarChar, 100).Value =
            (object?)patientLastName ?? DBNull.Value;

        await using var reader = await command.ExecuteReaderAsync();

        var results = new List<AppointmentListDto>();
        while (await reader.ReadAsync())
        {
            results.Add(new AppointmentListDto
            {
                IdAppointment  = reader.GetInt32(0),
                AppointmentDate = reader.GetDateTime(1),
                Status         = reader.GetString(2),
                Reason         = reader.GetString(3),
                PatientFullName = reader.GetString(4),
                PatientEmail   = reader.GetString(5)
            });
        }

        return results;
    }
    

    public async Task<AppointmentDetailsDto?> GetAppointmentByIdAsync(int idAppointment)
    {
        const string sql = """
            SELECT
                a.IdAppointment,
                a.AppointmentDate,
                a.Status,
                a.Reason,
                a.InternalNotes,
                a.CreatedAt,
                p.FirstName + N' ' + p.LastName AS PatientFullName,
                p.Email   AS PatientEmail,
                p.Phone   AS PatientPhone,
                d.FirstName + N' ' + d.LastName AS DoctorFullName,
                d.LicenseNumber,
                s.Name    AS Specialization
            FROM dbo.Appointments a
            JOIN dbo.Patients      p ON p.IdPatient = a.IdPatient
            JOIN dbo.Doctors       d ON d.IdDoctor  = a.IdDoctor
            JOIN dbo.Specializations s ON s.IdSpecialization = d.IdSpecialization
            WHERE a.IdAppointment = @IdAppointment;
            """;

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@IdAppointment", SqlDbType.Int).Value = idAppointment;

        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
            return null;

        return new AppointmentDetailsDto
        {
            IdAppointment   = reader.GetInt32(0),
            AppointmentDate = reader.GetDateTime(1),
            Status          = reader.GetString(2),
            Reason          = reader.GetString(3),
            InternalNotes   = reader.IsDBNull(4) ? null : reader.GetString(4),
            CreatedAt       = reader.GetDateTime(5),
            PatientFullName = reader.GetString(6),
            PatientEmail    = reader.GetString(7),
            PatientPhone    = reader.IsDBNull(8) ? null : reader.GetString(8),
            DoctorFullName  = reader.GetString(9),
            DoctorLicenseNumber = reader.GetString(10),
            Specialization  = reader.GetString(11)
        };
    }


    public async Task<ServiceResult> CreateAppointmentAsync(CreateAppointmentRequestDto request)
    {
        if (request.AppointmentDate <= DateTime.Now)
            return ServiceResult.Fail("Appointment date cannot be in the past.", ServiceErrorType.BadRequest);


        if (string.IsNullOrWhiteSpace(request.Reason))
            return ServiceResult.Fail("Reason cannot be empty.", ServiceErrorType.BadRequest);

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();


        if (!await EntityExistsAndActiveAsync(connection, "Patients", "IdPatient", request.IdPatient))
            return ServiceResult.Fail("Patient does not exist or is not active.", ServiceErrorType.BadRequest);


        if (!await EntityExistsAndActiveAsync(connection, "Doctors", "IdDoctor", request.IdDoctor))
            return ServiceResult.Fail("Doctor does not exist or is not active.", ServiceErrorType.BadRequest);


        if (await DoctorHasConflictAsync(connection, request.IdDoctor, request.AppointmentDate, excludeId: null))
            return ServiceResult.Fail(
                "The doctor already has a scheduled appointment at this time.",
                ServiceErrorType.Conflict);

        const string insertSql = """
            INSERT INTO dbo.Appointments
                (IdPatient, IdDoctor, AppointmentDate, Status, Reason, CreatedAt)
            VALUES
                (@IdPatient, @IdDoctor, @AppointmentDate, N'Scheduled', @Reason, GETDATE());
            SELECT CAST(SCOPE_IDENTITY() AS INT);
            """;

        await using var cmd = new SqlCommand(insertSql, connection);
        cmd.Parameters.Add("@IdPatient",       SqlDbType.Int).Value          = request.IdPatient;
        cmd.Parameters.Add("@IdDoctor",        SqlDbType.Int).Value          = request.IdDoctor;
        cmd.Parameters.Add("@AppointmentDate", SqlDbType.DateTime2).Value    = request.AppointmentDate;
        cmd.Parameters.Add("@Reason",          SqlDbType.NVarChar, 250).Value = request.Reason;

        var newId = (int)(await cmd.ExecuteScalarAsync())!;
        return ServiceResult.Ok(newId);
    }
    

    public async Task<ServiceResult> UpdateAppointmentAsync(
        int idAppointment, UpdateAppointmentRequestDto request)
    {
        var validStatuses = new[] { "Scheduled", "Completed", "Cancelled" };
        if (!validStatuses.Contains(request.Status))
            return ServiceResult.Fail(
                "Status must be one of: Scheduled, Completed, Cancelled.",
                ServiceErrorType.BadRequest);

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        
        var current = await GetAppointmentByIdAsync(idAppointment);   // reuse helper
        if (current is null)
            return ServiceResult.Fail("Appointment not found.", ServiceErrorType.NotFound);
        
        if (current.Status == "Completed" &&
            request.AppointmentDate != current.AppointmentDate)
            return ServiceResult.Fail(
                "Cannot change the date of a completed appointment.",
                ServiceErrorType.Conflict);
        
        if (!await EntityExistsAndActiveAsync(connection, "Patients", "IdPatient", request.IdPatient))
            return ServiceResult.Fail("Patient does not exist or is not active.", ServiceErrorType.BadRequest);

        if (!await EntityExistsAndActiveAsync(connection, "Doctors", "IdDoctor", request.IdDoctor))
            return ServiceResult.Fail("Doctor does not exist or is not active.", ServiceErrorType.BadRequest);
        
        if (await DoctorHasConflictAsync(connection, request.IdDoctor,
                request.AppointmentDate, excludeId: idAppointment))
            return ServiceResult.Fail(
                "The doctor already has a scheduled appointment at this time.",
                ServiceErrorType.Conflict);

        const string updateSql = """
            UPDATE dbo.Appointments SET
                IdPatient       = @IdPatient,
                IdDoctor        = @IdDoctor,
                AppointmentDate = @AppointmentDate,
                Status          = @Status,
                Reason          = @Reason,
                InternalNotes   = @InternalNotes
            WHERE IdAppointment = @IdAppointment;
            """;

        await using var cmd = new SqlCommand(updateSql, connection);
        cmd.Parameters.Add("@IdPatient",       SqlDbType.Int).Value           = request.IdPatient;
        cmd.Parameters.Add("@IdDoctor",        SqlDbType.Int).Value           = request.IdDoctor;
        cmd.Parameters.Add("@AppointmentDate", SqlDbType.DateTime2).Value     = request.AppointmentDate;
        cmd.Parameters.Add("@Status",          SqlDbType.NVarChar, 20).Value  = request.Status;
        cmd.Parameters.Add("@Reason",          SqlDbType.NVarChar, 250).Value = request.Reason;
        cmd.Parameters.Add("@InternalNotes",   SqlDbType.NVarChar, -1).Value  =
            (object?)request.InternalNotes ?? DBNull.Value;
        cmd.Parameters.Add("@IdAppointment",   SqlDbType.Int).Value           = idAppointment;

        await cmd.ExecuteNonQueryAsync();
        return ServiceResult.Ok();
    }



    public async Task<ServiceResult> DeleteAppointmentAsync(int idAppointment)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();


        const string selectSql =
            "SELECT Status FROM dbo.Appointments WHERE IdAppointment = @IdAppointment;";

        await using var selectCmd = new SqlCommand(selectSql, connection);
        selectCmd.Parameters.Add("@IdAppointment", SqlDbType.Int).Value = idAppointment;

        var status = (string?)await selectCmd.ExecuteScalarAsync();

        if (status is null)
            return ServiceResult.Fail("Appointment not found.", ServiceErrorType.NotFound);

        if (status == "Completed")
            return ServiceResult.Fail(
                "Cannot delete a completed appointment.",
                ServiceErrorType.Conflict);

        const string deleteSql =
            "DELETE FROM dbo.Appointments WHERE IdAppointment = @IdAppointment;";

        await using var deleteCmd = new SqlCommand(deleteSql, connection);
        deleteCmd.Parameters.Add("@IdAppointment", SqlDbType.Int).Value = idAppointment;
        await deleteCmd.ExecuteNonQueryAsync();

        return ServiceResult.Ok();
    }
    

    private static async Task<bool> EntityExistsAndActiveAsync(
        SqlConnection connection, string table, string idColumn, int id)
    {

        var sql = $"SELECT COUNT(1) FROM dbo.{table} WHERE {idColumn} = @Id AND IsActive = 1;";
        await using var cmd = new SqlCommand(sql, connection);
        cmd.Parameters.Add("@Id", SqlDbType.Int).Value = id;
        var count = (int)(await cmd.ExecuteScalarAsync())!;
        return count > 0;
    }

    private static async Task<bool> DoctorHasConflictAsync(
        SqlConnection connection, int idDoctor, DateTime appointmentDate, int? excludeId)
    {
        const string sql = """
            SELECT COUNT(1)
            FROM dbo.Appointments
            WHERE IdDoctor        = @IdDoctor
              AND AppointmentDate = @AppointmentDate
              AND Status          = N'Scheduled'
              AND (@ExcludeId IS NULL OR IdAppointment <> @ExcludeId);
            """;

        await using var cmd = new SqlCommand(sql, connection);
        cmd.Parameters.Add("@IdDoctor",        SqlDbType.Int).Value      = idDoctor;
        cmd.Parameters.Add("@AppointmentDate", SqlDbType.DateTime2).Value = appointmentDate;
        cmd.Parameters.Add("@ExcludeId",       SqlDbType.Int).Value      =
            excludeId.HasValue ? excludeId.Value : DBNull.Value;

        var count = (int)(await cmd.ExecuteScalarAsync())!;
        return count > 0;
    }
}