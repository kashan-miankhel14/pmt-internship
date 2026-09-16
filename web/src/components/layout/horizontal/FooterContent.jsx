'use client'

// Util Imports
import { horizontalLayoutClasses } from '@layouts/utils/layoutClasses'

const FooterContent = () => {
  return (
    <div className={horizontalLayoutClasses.footerContent}>
      <p>
        <span className='text-textSecondary'>{`© ${new Date().getFullYear()} Kashan Saeed. All rights reserved.`}</span>
      </p>
    </div>
  )
}

export default FooterContent
