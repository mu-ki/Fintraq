using ExpenseManager.Models.Chat;
using ExpenseManager.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace ExpenseManager.Controllers;

[ApiController]
[Authorize]
[Route("api/chat")]
public class ChatApiController(
    IChatAssistantService chatAssistantService,
    UserManager<IdentityUser> userManager) : ControllerBase
{
    [HttpPost("query")]
    public async Task<ActionResult<ChatQueryResponse>> Query([FromBody] ChatQueryRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var userId = userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Challenge();
        }

        var response = await chatAssistantService.HandleAsync(userId, request, cancellationToken);
        return Ok(response);
    }
}
