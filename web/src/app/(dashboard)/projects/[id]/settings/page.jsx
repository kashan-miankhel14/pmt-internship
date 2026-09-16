import { redirect } from 'next/navigation'

const SettingsPage = async props => {
  const params = await props.params
  redirect(`/projects/${params.id}/settings/access`)
}

export default SettingsPage
