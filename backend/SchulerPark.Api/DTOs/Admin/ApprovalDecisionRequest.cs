namespace SchulerPark.Api.DTOs.Admin;

using System.ComponentModel.DataAnnotations;

public record ApprovalDecisionRequest([Required] bool? Approve);
