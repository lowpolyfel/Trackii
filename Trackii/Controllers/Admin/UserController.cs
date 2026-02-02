using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Trackii.Models.Admin.User;
using Trackii.Services.Admin;

namespace Trackii.Controllers.Admin
{
    [Authorize(Roles = "Admin")]
    [Route("Admin/User")]
    public class UserController : Controller
    {
        private readonly UserService _service;
        private const string ViewBase = "~/Views/Management/User/";

        public UserController(UserService service)
        {
            _service = service;
        }

        [HttpGet("")]
        public IActionResult Index(string? search, bool showInactive = false, int page = 1)
        {
            var vm = _service.GetPaged(search, showInactive, page, 10);
            return View($"{ViewBase}Index.cshtml", vm);
        }

        [HttpGet("Create")]
        public IActionResult Create()
        {
            LoadRoles();
            return View($"{ViewBase}Create.cshtml", new UserCreateVm());
        }

        [HttpPost("Create")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(UserCreateVm vm)
        {
            if (!ModelState.IsValid)
            {
                LoadRoles();
                return View($"{ViewBase}Create.cshtml", vm);
            }

            var errors = await _service.CreateAsync(vm);
            if (errors.Any())
            {
                foreach (var err in errors)
                    ModelState.AddModelError(nameof(vm.Password), err);

                LoadRoles();
                return View($"{ViewBase}Create.cshtml", vm);
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpGet("Edit/{id:long}")]
        public IActionResult Edit(uint id)
        {
            var vm = _service.GetById(id);
            if (vm == null)
                return NotFound();

            LoadRoles();
            return View($"{ViewBase}Edit.cshtml", vm);
        }

        [HttpPost("Edit/{id:long}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(uint id, UserEditVm vm)
        {
            if (id != vm.Id)
                return BadRequest();

            if (!ModelState.IsValid)
            {
                LoadRoles();
                return View($"{ViewBase}Edit.cshtml", vm);
            }

            var errors = await _service.UpdateAsync(vm);
            if (errors.Any())
            {
                foreach (var err in errors)
                    ModelState.AddModelError(nameof(vm.NewPassword), err);

                LoadRoles();
                return View($"{ViewBase}Edit.cshtml", vm);
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost("Toggle")]
        [ValidateAntiForgeryToken]
        public IActionResult Toggle(uint id)
        {
            _service.Toggle(id);
            return RedirectToAction(nameof(Index));
        }

        private void LoadRoles()
        {
            ViewBag.Roles = _service.GetRoles();
        }
    }
}
