/** @type {import('next').NextConfig} */
const nextConfig = {
    basePath: process.env.BASEPATH,
    redirects: async () => {
        return [
            {
                source: '/',
                destination: '/home',
                permanent: true,
                locale: false
            },
            {
                source: '/about',
                destination: '/home',
                permanent: true,
                locale: false
            }
        ];
    }
};
export default nextConfig;
