using Microsoft.AspNetCore.Mvc;
using Tutorial7.DTOs;
using Tutorial7.Services;

namespace Tutorial7.Controllers;

[Route("api/[controller]")]
[ApiController]
public class AppointmentsController : ControllerBase
{
    private readonly IAppointmentsService _appointmentsService;

    public AppointmentsController(IAppointmentsService appointmentsService)
    {
        _appointmentsService = appointmentsService;
    }
    
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? status,
        [FromQuery] string? patientLastName)
    {
        var appointments = await _appointmentsService.GetAllAppointmentsAsync(status, patientLastName);
        return Ok(appointments);
    }

    [HttpGet("{idAppointment:int}")]
    public async Task<IActionResult> GetById(int idAppointment)
    {
        var appointment = await _appointmentsService.GetAppointmentByIdAsync(idAppointment);
        if (appointment is null)
            return NotFound(new ErrorResponseDto { Message = $"Appointment {idAppointment} not found." });

        return Ok(appointment);
    }

  
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateAppointmentRequestDto request)
    {
        var result = await _appointmentsService.CreateAppointmentAsync(request);

        if (!result.IsSuccess)
            return result.ErrorType switch
            {
                ServiceErrorType.BadRequest => BadRequest(new ErrorResponseDto { Message = result.ErrorMessage }),
                ServiceErrorType.Conflict   => Conflict(new ErrorResponseDto { Message = result.ErrorMessage }),
                _                           => StatusCode(500)
            };

        return CreatedAtAction(nameof(GetById), new { idAppointment = result.NewId },
            new { idAppointment = result.NewId });
    }


    [HttpPut("{idAppointment:int}")]
    public async Task<IActionResult> Update(int idAppointment,
        [FromBody] UpdateAppointmentRequestDto request)
    {
        var result = await _appointmentsService.UpdateAppointmentAsync(idAppointment, request);

        if (!result.IsSuccess)
            return result.ErrorType switch
            {
                ServiceErrorType.NotFound   => NotFound(new ErrorResponseDto { Message = result.ErrorMessage }),
                ServiceErrorType.BadRequest => BadRequest(new ErrorResponseDto { Message = result.ErrorMessage }),
                ServiceErrorType.Conflict   => Conflict(new ErrorResponseDto { Message = result.ErrorMessage }),
                _                           => StatusCode(500)
            };

        return Ok();
    }


    [HttpDelete("{idAppointment:int}")]
    public async Task<IActionResult> Delete(int idAppointment)
    {
        var result = await _appointmentsService.DeleteAppointmentAsync(idAppointment);

        if (!result.IsSuccess)
            return result.ErrorType switch
            {
                ServiceErrorType.NotFound => NotFound(new ErrorResponseDto { Message = result.ErrorMessage }),
                ServiceErrorType.Conflict => Conflict(new ErrorResponseDto { Message = result.ErrorMessage }),
                _                         => StatusCode(500)
            };

        return NoContent();
    }
}

