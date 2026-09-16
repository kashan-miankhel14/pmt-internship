'use client'

import { createContext, useContext, useMemo } from 'react'

import { buildAbility } from '@/libs/ability'

const AbilityContext = createContext(buildAbility())

export const Can = ({ I, a, children }) => {
  const ability = useAbility()

  return ability.can(I, a) ? children : null
}

export const AbilityProvider = ({ permissions, children }) => {
  const ability = useMemo(() => buildAbility(permissions), [permissions])

  return <AbilityContext.Provider value={ability}>{children}</AbilityContext.Provider>
}

export const useAbility = () => useContext(AbilityContext)
