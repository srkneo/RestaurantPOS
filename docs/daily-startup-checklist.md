# AllInOne POS - Daily Startup Checklist

**1. Start the Database (PostgreSQL via Docker)**

- Open **Docker Desktop** from the Windows Start menu and let the engine initialize.
- Open PowerShell and start the container:PowerShell
    
    ```
    docker start pos-postgres
    ```
    
    *(Verify: `docker ps` should show it running on port 5433).*
    

**2. Start the Local LLM (Ollama)**

- Launch the **Ollama** app from the Windows Start menu.
- Alternatively, open a PowerShell tab and run:PowerShell
    
    ```
    ollama serve
    ```
    
    *(Verify: It will display "listening on 127.0.0.1:11434". Leave this tab open).*
    

**3. Start the Backend (ASP.NET Core)**

- Open a new PowerShell tab and navigate to the project root:PowerShell
    
    ```
    cd C:\Projects\RestaurantPOS
    dotnet run
    ```
    
    *(Verify: The console will show the NLog startup message and list the active localhost ports).*
    

**4. Start the Frontend (React)**

- Open a new PowerShell tab and navigate to the React app:PowerShell
    
    ```
    cd C:\Projects\RestaurantPOS\ClientApp\ClientApp
    npm run dev
    ```
    
    *(Verify: The terminal will provide a localhost link, usually `http://localhost:5173`, to open in your browser).*
    

**5. Start the AI Assistant (Aider)**

- Open a final PowerShell tab in the project root:PowerShell
    
    ```
    cd C:\Projects\RestaurantPOS
    ```
    
- *(Optional)* If you use a virtual environment, activate it:PowerShell
    
    ```
    .\venv\Scripts\Activate
    ```
    
- Launch Aider using Python (to bypass any Windows PATH issues):PowerShell
    
    ```
    python -m aider --model ollama/qwen2.5-coder:14b
    ```
