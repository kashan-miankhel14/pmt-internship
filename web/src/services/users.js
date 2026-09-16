import { endpoints } from '@/api/endpoints'
import { httpGet, httpPut } from '@/api/httpClient'
import { optionalId, optionalText, requiredText, toBool } from '@/libs/payload'
import { createCrudService } from '@/services/crud'

// UpsertUserRequest(DepartmentId, UserName, Email, DisplayName, Password, Active, RoleId)
const toRequest = body => ({
  departmentId: optionalId(body.departmentId),
  userName: requiredText(body.userName),
  email: requiredText(body.email),
  displayName: requiredText(body.displayName),

  // A blank password means "leave unchanged" — the validator only checks length when one is sent.
  password: optionalText(body.password),
  active: toBool(body.active),
  roleId: optionalId(body.roleId)
})

const crud = createCrudService({ path: endpoints.users.base, toRequest })

export const usersService = {
  ...crud,
  availableRoles: () => httpGet(endpoints.users.availableRoles),

  /**
   * Typeahead read used by UserPicker. A 403 here must not bounce the whole screen to
   * /forbidden the way a module list does — a project admin without `users.view` should just
   * see an empty dropdown, not lose the page they were working on.
   */
  search: ({ search, pageSize = 25 } = {}) =>
    httpGet(endpoints.users.base, {
      params: { page: 1, pageSize, ...(search ? { search } : {}) },
      __suppressForbidden: true
    }).catch(() => ({ items: [] })),

  // Both endpoints speak RoleDto[]; SetUserRolesRequest wraps the ids.
  getRoles: userId => httpGet(endpoints.users.roles(userId)),
  setRoles: (userId, roleIds) => httpPut(endpoints.users.roles(userId), { roleIds: roleIds.map(Number) })
}
