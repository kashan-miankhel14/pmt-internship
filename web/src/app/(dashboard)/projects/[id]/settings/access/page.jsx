import ProjectAccessSettings from '@views/projects/ProjectAccessSettings'

const Page = async props => {
  const params = await props.params

  return <ProjectAccessSettings projectId={params.id} />
}

export default Page
