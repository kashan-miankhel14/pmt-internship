import { endpoints } from '@/api/endpoints'
import { optionalText, requiredText, toBool } from '@/libs/payload'
import { createCrudService } from '@/services/crud'

// UpsertDepartmentRequest(Code, Name, Description, Active)
const toRequest = body => ({
  code: requiredText(body.code),
  name: requiredText(body.name),
  description: optionalText(body.description),
  active: toBool(body.active)
})

export const departmentsService = createCrudService({ path: endpoints.departments, toRequest })
