import React from 'react';
import CircularProgress from '@mui/material/CircularProgress';
import Box from '@mui/material/Box';

export const Loader = () => (
  <Box sx={{display:'flex',alignItems:'center',justifyContent:'center',width:'100%',height:'100%',minHeight:'150px'}}>
    <CircularProgress size={48} color="primary" />
  </Box>
);

export default Loader;
