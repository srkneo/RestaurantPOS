#!/bin/bash

# Navigate to the root directory of the project
cd path/to

# Create a new solution file
dotnet new sln -n RestaurantPOS

# Add the project to the solution
dotnet sln add RestaurantPOS.csproj
