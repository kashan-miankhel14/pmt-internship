import TeamDetail from '@views/teams/TeamDetail'

const Page = async props => {
  const params = await props.params

  return <TeamDetail teamId={params.id} />
}

export default Page
