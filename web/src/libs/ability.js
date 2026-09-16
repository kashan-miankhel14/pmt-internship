import { AbilityBuilder, createMongoAbility } from '@casl/ability'

export const buildAbility = (permissions = []) => {
  const { can, build } = new AbilityBuilder(createMongoAbility)

  permissions.forEach(permission => {
    const [subject, action] = permission.split('.')

    if (subject && action) can(action, subject)
  })

  return build()
}
