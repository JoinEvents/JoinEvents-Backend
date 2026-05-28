## Run these commands from the backend solution folder:
##   c:\D_drive\EventEase\EventEase\

# 1. Create the EF Core migration
dotnet ef migrations add AddEventCategories `
  --project EventEase.Infrastructure\EventEase.Infrastructure.csproj `
  --startup-project EventEase\EventEase.Api.csproj

# 2. Apply migration to the database
dotnet ef database update `
  --project EventEase.Infrastructure\EventEase.Infrastructure.csproj `
  --startup-project EventEase\EventEase.Api.csproj

# 3. Build to verify everything compiles
dotnet build EventEase.sln
