'use client'

// Util Imports
import { verticalLayoutClasses } from '@layouts/utils/layoutClasses'

const FooterContent = () => {
  return (
    <div className={verticalLayoutClasses.footerContent}>
      <p>
        <span className='text-textSecondary'>{`© ${new Date().getFullYear()} Kashan Saeed. All rights reserved.`}</span>
      </p>
    </div>
  )
}

export default FooterContent
