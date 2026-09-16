// Next Imports
import dynamic from 'next/dynamic'

// MUI Imports
import Button from '@mui/material/Button'

// Layout Imports
import LayoutWrapper from '@layouts/LayoutWrapper'
import VerticalLayout from '@layouts/VerticalLayout'
import HorizontalLayout from '@layouts/HorizontalLayout'

// Component Imports
import Providers from '@components/Providers'
import Navigation from '@components/layout/vertical/Navigation'
import Header from '@components/layout/horizontal/Header'
import Navbar from '@components/layout/vertical/Navbar'
import VerticalFooter from '@components/layout/vertical/Footer'
import HorizontalFooter from '@components/layout/horizontal/Footer'
import ScrollToTop from '@core/components/scroll-to-top'
import AuthGuard from '@components/AuthGuard'

// Util Imports
import { getMode, getSystemMode } from '@core/utils/serverHelpers'

/*
  Code-split out of the layout bundle. The assistant is mounted on every authenticated page but
  is idle until its launcher is clicked, so its chunk (drawer, conversation sidebar, markdown
  renderer, composer) has no reason to sit in the JavaScript every dashboard page has to parse
  before it can hydrate.
*/
const AiChatPanel = dynamic(() => import('@components/ai/AiChatPanel'))

const Layout = async props => {
  const { children } = props

  // Type guard to ensure lang is a valid Locale
  // Vars
  const direction = 'ltr'
  const mode = await getMode()
  const systemMode = await getSystemMode()

  return (
    <Providers direction={direction}>
      <AuthGuard>
        <LayoutWrapper
          systemMode={systemMode}
          verticalLayout={
            <VerticalLayout navigation={<Navigation mode={mode} />} navbar={<Navbar />} footer={<VerticalFooter />}>
              {children}
            </VerticalLayout>
          }
          horizontalLayout={
            <HorizontalLayout header={<Header />} footer={<HorizontalFooter />}>
              {children}
            </HorizontalLayout>
          }
        />
        {/*
          Mounted inside AuthGuard so it only ever exists for a signed-in shell (the panel
          reads the session for the admin reindex button and to key its stored conversation
          id). It renders nothing in page flow: a fixed launcher plus a drawer.
        */}
        <AiChatPanel />
      </AuthGuard>
      {/*
        The launcher sits in the bottom-inline-end corner, so the scroll-to-top button is
        raised above it. Without this both fixed controls occupy the same corner and overlap
        once the page is scrolled past its threshold.
      */}
      <ScrollToTop className='mui-fixed bottom-[5.5rem]'>
        <Button variant='contained' className='is-10 bs-10 rounded-full p-0 min-is-0 flex items-center justify-center'>
          <i className='tabler-arrow-up' />
        </Button>
      </ScrollToTop>
    </Providers>
  )
}

export default Layout
