'use client'

// React Imports
import { useEffect, useMemo, useState } from 'react'

// Next Imports
import Link from 'next/link'
import { useRouter } from 'next/navigation'

// MUI Imports
import Button from '@mui/material/Button'
import Card from '@mui/material/Card'
import CardContent from '@mui/material/CardContent'
import Divider from '@mui/material/Divider'
import Grid from '@mui/material/Grid'
import Step from '@mui/material/Step'
import StepLabel from '@mui/material/StepLabel'
import Stepper from '@mui/material/Stepper'
import Typography from '@mui/material/Typography'

// Third-party Imports
import { useMutation } from '@tanstack/react-query'
import { toast } from 'react-toastify'

// Component Imports
import CustomTextField from '@core/components/mui/TextField'
import EmptyState from '@/components/pmt/EmptyState'
import PageHeader from '@/components/pmt/PageHeader'
import StepperCustomDot from '@components/stepper-dot'
import StepperWrapper from '@core/styles/stepper'
import UserPicker from '@/components/pmt/UserPicker'

// Hook Imports
import { useAbility } from '@/contexts/AbilityContext'
import { useAuth } from '@/contexts/AuthContext'
import { useLookups } from '@/hooks/useLookups'

// Lib Imports
import { extractErrors } from '@/libs/errors'
import { KEY_PATTERN, accessLevels, deriveKey, projectTemplates } from '@/libs/projectTemplates'

// Service Imports
import { projectAccessService } from '@/services/projectAccess'

const steps = [
  { title: 'Template & details', subtitle: 'How the project works' },
  { title: 'Lead & access', subtitle: 'Who runs and sees it' },
  { title: 'Review', subtitle: 'Confirm and create' }
]

/**
 * One selectable card used for both the template and the access-level choice. Rendered as a
 * radio group in spirit: exactly one option is active and the whole card is the hit area.
 */
const OptionCard = ({ option, selected, onSelect }) => (
  <Card
    role='radio'
    aria-checked={selected}
    tabIndex={0}
    onClick={() => onSelect(option.code)}
    onKeyDown={event => {
      if (event.key === 'Enter' || event.key === ' ') {
        event.preventDefault()
        onSelect(option.code)
      }
    }}
    sx={{
      cursor: 'pointer',
      blockSize: '100%',
      borderWidth: 2,
      borderStyle: 'solid',
      borderColor: selected ? 'primary.main' : 'transparent',
      boxShadow: selected ? 'var(--mui-customShadows-primary-sm)' : undefined,
      transition: 'border-color 200ms ease, box-shadow 200ms ease'
    }}
  >
    <CardContent className='flex flex-col gap-2'>
      <div className='flex items-center gap-3'>
        <div
          className={`flex is-10 bs-10 items-center justify-center rounded-lg ${
            selected ? 'bg-primary text-white' : 'bg-actionHover text-textSecondary'
          }`}
        >
          <i className={option.icon} />
        </div>
        <Typography variant='h6'>{option.name}</Typography>
      </div>
      <Typography variant='body2' color='text.secondary'>
        {option.description}
      </Typography>
      {option.columns ? (
        <Typography variant='caption' color='text.disabled'>
          Board: {option.columns}
        </Typography>
      ) : null}
    </CardContent>
  </Card>
)

const ReviewRow = ({ label, children }) => (
  <Grid size={{ xs: 12, sm: 6 }}>
    <Typography variant='body2' color='text.secondary'>
      {label}
    </Typography>
    <Typography className='mbs-1'>{children}</Typography>
  </Grid>
)

const ProjectWizard = () => {
  const router = useRouter()
  const ability = useAbility()
  const { session } = useAuth()
  const { data: lookups } = useLookups()

  const [activeStep, setActiveStep] = useState(0)
  const [templateCode, setTemplateCode] = useState('SCRUM')
  const [accessLevel, setAccessLevel] = useState('RESTRICTED')
  const [name, setName] = useState('')
  const [projectKey, setProjectKey] = useState('')
  const [keyEdited, setKeyEdited] = useState(false)
  const [description, setDescription] = useState('')
  const [lead, setLead] = useState(null)

  // The lead defaults to the creator (spec stage 3); the session carries enough of the user
  // record for the picker to render it without another request.
  useEffect(() => {
    if (lead || !session?.userId) return

    setLead({
      id: session.userId,
      displayName: session.displayName,
      userName: session.userName,
      email: session.email
    })
  }, [session, lead])

  // Suggest the key from the name until the user types one themselves.
  useEffect(() => {
    if (keyEdited) return

    setProjectKey(deriveKey(name))
  }, [name, keyEdited])

  const takenKeys = useMemo(
    () => new Set((lookups?.projects ?? []).map(project => String(project.key ?? '').toUpperCase())),
    [lookups?.projects]
  )

  const keyError = useMemo(() => {
    if (!projectKey) return 'A project key is required.'
    if (!KEY_PATTERN.test(projectKey)) return '2-10 characters, A-Z or 0-9, starting with a letter.'
    if (takenKeys.has(projectKey)) return 'Another project already uses this key.'

    return null
  }, [projectKey, takenKeys])

  const createMutation = useMutation({
    mutationFn: projectAccessService.createFromTemplate,
    onSuccess: project => {
      toast.success(`${project?.name ?? name} created.`)

      // Detail routes are keyed by id today; a key-only payload still deep-links correctly.
      router.push(`/projects/${project?.id ?? project?.key ?? ''}`)
    },
    onError: error => {
      if (error.response?.status === 403) return

      toast.error(extractErrors(error.response?.data ?? { errors: [error.message] })[0])
    }
  })

  if (ability.cannot('manage', 'projects')) {
    return <EmptyState title='Unauthorized' description='You do not have permission to create projects.' />
  }

  const stepValid = [Boolean(name.trim()) && !keyError, Boolean(lead?.id) && Boolean(accessLevel), true][activeStep]

  const selectedTemplate = projectTemplates.find(item => item.code === templateCode)
  const selectedAccess = accessLevels.find(item => item.code === accessLevel)

  const handleCreate = () =>
    createMutation.mutate({
      key: projectKey,
      name: name.trim(),
      description,
      templateCode,
      accessLevel,
      leadUserId: lead?.id
    })

  return (
    <div className='flex flex-col gap-6'>
      <PageHeader
        eyebrow='Stage 3 · Project creation'
        title='Create a project'
        description='Pick a template, name the project and decide who can reach it. The template seeds the default board, its columns and the issue types.'
        actions={
          <Button component={Link} href='/projects' variant='tonal' startIcon={<i className='tabler-arrow-left' />}>
            Back to projects
          </Button>
        }
      />

      <Card>
        <CardContent>
          <StepperWrapper>
            <Stepper activeStep={activeStep} alternativeLabel>
              {steps.map((step, index) => (
                <Step key={step.title} completed={activeStep > index}>
                  <StepLabel slots={{ stepIcon: StepperCustomDot }}>
                    <div className='step-label'>
                      <div className='flex flex-col'>
                        <Typography className='step-title'>{step.title}</Typography>
                        <Typography className='step-subtitle'>{step.subtitle}</Typography>
                      </div>
                    </div>
                  </StepLabel>
                </Step>
              ))}
            </Stepper>
          </StepperWrapper>
        </CardContent>
      </Card>

      <Card>
        <CardContent className='flex flex-col gap-6'>
          {activeStep === 0 ? (
            <>
              <div>
                <Typography variant='h6'>Choose a template</Typography>
                <Typography variant='body2' color='text.secondary'>
                  The template decides the board type and its starting columns. It can be changed later in project
                  settings.
                </Typography>
              </div>
              <Grid container spacing={4} role='radiogroup' aria-label='Project template'>
                {projectTemplates.map(template => (
                  <Grid key={template.code} size={{ xs: 12, md: 4 }}>
                    <OptionCard option={template} selected={templateCode === template.code} onSelect={setTemplateCode} />
                  </Grid>
                ))}
              </Grid>

              <Divider />

              <Grid container spacing={4}>
                <Grid size={{ xs: 12, md: 8 }}>
                  <CustomTextField
                    fullWidth
                    required
                    label='Project name'
                    placeholder='UDL Project Management Tool'
                    value={name}
                    onChange={event => setName(event.target.value)}
                  />
                </Grid>
                <Grid size={{ xs: 12, md: 4 }}>
                  <CustomTextField
                    fullWidth
                    required
                    label='Project key'
                    placeholder='UPMT'
                    value={projectKey}
                    onChange={event => {
                      setKeyEdited(true)
                      setProjectKey(event.target.value.toUpperCase())
                    }}
                    error={Boolean(name && keyError)}
                    helperText={
                      (name && keyError) ||
                      'Prefixes every issue (UPMT-1). Immutable once the first issue exists.'
                    }
                  />
                </Grid>
                <Grid size={{ xs: 12 }}>
                  <CustomTextField
                    fullWidth
                    multiline
                    minRows={3}
                    label='Description'
                    placeholder='What this project delivers.'
                    value={description}
                    onChange={event => setDescription(event.target.value)}
                  />
                </Grid>
              </Grid>
            </>
          ) : null}

          {activeStep === 1 ? (
            <>
              <Grid container spacing={4}>
                <Grid size={{ xs: 12, md: 6 }}>
                  <UserPicker
                    label='Project lead'
                    required
                    value={lead}
                    onChange={setLead}
                    helperText='The lead is seated as a Project Admin when the project is created.'
                  />
                </Grid>
              </Grid>

              <Divider />

              <div>
                <Typography variant='h6'>Access level</Typography>
                <Typography variant='body2' color='text.secondary'>
                  Access can be widened or tightened later, and individual members and teams are granted in project
                  settings.
                </Typography>
              </div>
              <Grid container spacing={4} role='radiogroup' aria-label='Access level'>
                {accessLevels.map(level => (
                  <Grid key={level.code} size={{ xs: 12, md: 4 }}>
                    <OptionCard option={level} selected={accessLevel === level.code} onSelect={setAccessLevel} />
                  </Grid>
                ))}
              </Grid>
            </>
          ) : null}

          {activeStep === 2 ? (
            <>
              <div>
                <Typography variant='h6'>Review</Typography>
                <Typography variant='body2' color='text.secondary'>
                  Creating the project also seeds its issue counter, default board and columns in one transaction.
                </Typography>
              </div>
              <Grid container spacing={4}>
                <ReviewRow label='Template'>
                  {selectedTemplate?.name} — {selectedTemplate?.columns}
                </ReviewRow>
                <ReviewRow label='Access level'>
                  {selectedAccess?.name} — {selectedAccess?.description}
                </ReviewRow>
                <ReviewRow label='Name'>{name}</ReviewRow>
                <ReviewRow label='Key'>{projectKey}</ReviewRow>
                <ReviewRow label='Lead'>{lead?.displayName ?? lead?.userName ?? '--'}</ReviewRow>
                <ReviewRow label='Description'>{description || 'No description added.'}</ReviewRow>
              </Grid>
            </>
          ) : null}

          <Divider />

          <div className='flex items-center justify-between gap-3'>
            <Button
              variant='tonal'
              color='secondary'
              disabled={activeStep === 0 || createMutation.isPending}
              onClick={() => setActiveStep(step => step - 1)}
              startIcon={<i className='tabler-arrow-left' />}
            >
              Back
            </Button>
            {activeStep === steps.length - 1 ? (
              <Button
                variant='contained'
                onClick={handleCreate}
                disabled={createMutation.isPending || Boolean(keyError) || !name.trim() || !lead?.id}
                startIcon={<i className='tabler-check' />}
              >
                {createMutation.isPending ? 'Creating…' : 'Create project'}
              </Button>
            ) : (
              <Button
                variant='contained'
                onClick={() => setActiveStep(step => step + 1)}
                disabled={!stepValid}
                endIcon={<i className='tabler-arrow-right' />}
              >
                Next
              </Button>
            )}
          </div>
        </CardContent>
      </Card>
    </div>
  )
}

export default ProjectWizard
