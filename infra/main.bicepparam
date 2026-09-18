using 'main.bicep'

// Deployable image tag
param imageTag = 'latest'

// Production GitHub OAuth App (client id is public; the secret lives in the
// vault as Authentication--GitHub--ClientSecret and is read at runtime).
param githubClientId = 'your-github-client-id'

param allowedLogins = 'your-github-username'
