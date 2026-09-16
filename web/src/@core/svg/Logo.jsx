const Logo = ({ width = 30, height = 30, className, ...props }) => {
  return (
    <svg
      width={width}
      height={height}
      viewBox='0 0 32 32'
      fill='none'
      xmlns='http://www.w3.org/2000/svg'
      className={className}
      {...props}
    >
      <defs>
        <linearGradient id='zenithLogoGrad' x1='0%' y1='100%' x2='100%' y2='0%'>
          <stop offset='0%' stopColor='#142D4C' />
          <stop offset='100%' stopColor='#326DA3' />
        </linearGradient>
      </defs>
      <rect width='32' height='32' rx='7' fill='url(#zenithLogoGrad)' />
      <path
        d='M7.5 7.5H24.5L13.5 17.5H24.5V24.5H7.5L18.5 14.5H7.5V7.5Z'
        fill='#FFFFFF'
        fillRule='evenodd'
        clipRule='evenodd'
      />
    </svg>
  )
}

export default Logo
