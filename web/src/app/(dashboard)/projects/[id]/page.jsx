import ProjectDetail from '@views/projects/ProjectDetail'

const Page = async props => {
  const params = await props.params

  return <ProjectDetail projectId={params.id} />
}

export default Page
