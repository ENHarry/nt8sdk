# ATI DOCUMENTATION
## What can I do through automation?

•Place orders

•Initiate a NinjaTrader ATM Strategy

•Change orders

•Cancel orders

•Close ATM Strategies and positions

•Flatten accounts

•Cancel all orders

•Retrieve information on positions and orders

NinjaTrader provides three options for communicating from an external application to NinjaTrader for trade automation. The Email Interface requires absolutely no programming experience whatsoever, other options require various levels of programming/scripting experience.

## Understanding the three interface options

**TradeStation Email Interface**
The TradeStation Email Interface allows you to take advantage of TradeStation's email notification capabilities right out of the box. Run your TradeStation strategy in real time, order signals are emailed within your computer (never leaves your PC) to NinjaTrader which processes the order through to your broker.
 
**File Interface**
The File interface uses standard text files as input. These files are called order instruction files (OIF) and have specific format requirements. NinjaTrader processes the OIF the instant the file is written to the hard drive and subsequently deletes the file once the processing operation is complete.

**DLL Interface**
NinjaTrader provides a DLL named NtDirect.dll that supports various functions for automated trading.

## Which interface option should I use?

**TradeStation Systems**
•If you are not running your own strategies or you have limited or no programming experience you should use the TradeStation Email Interface
•If you are running your own system and you are comfortable with EasyLanguage and want to have bi-directional control of your real-time order processing you should use the DLL interface.

**Other Charting Applications**
•You should use the DLL if your charting application supports that interface type or use the File Interface
 
**Custom Applications**
•You should use the DLL interface

Commands and Valid Parameters
	Previous page Return to chapter overview Next page

The following section is only relevant for the File and DLL interfaces. Both interfaces share common interface functions/methods that take as arguments the parameters defined in the tables below. You can automate your trading through eight different commands. Command definitions are also provided below.

## Understanding parameters and valid values

### Available Parameters and Valid Values

![alt text](image-1.png)

### Available Commands
The following table displays required (R) and optional (O) values for each different command value.

 ![alt text](image.png)

      
### Understanding the commands
Following are the descriptions of each available command.

**CANCEL COMMAND**
This command will cancel an order and requires an order ID value and an optional strategy ID value. The order ID value must match either the order ID value given to an order placed through the PLACE command or, an order name such as ENTRY*, EXIT*, STOP*, SIMSTOP* or TARGET*. The star (*) represents an integer value such as TARGET1 or TARGET2. Order names are only valid if a valid strategy ID value is passed. The strategy ID value must match a strategy ID value given to a strategy in the PLACE command.
 
**CANCELALLORDERS COMMAND**
This command will cancel all active orders across all accounts and broker connections.

**CHANGE COMMAND**
This command will change the parameters of an order and requires an order ID value, optional price and quantity values and an optional strategy ID value. The order ID value must match either the order ID value given to an order placed through the PLACE command or, an order name such as ENTRY*, EXIT*, STOP*, SIMSTOP* or TARGET*. The star (*) represents an integer value such as TARGET1 or TARGET2. Order names are only valid if a valid strategy ID value is passed. Pass in zero (0) values for price and quantity if you do not wish to change these order parameters. Price values must be in US decimal format (1212.25 is correct while 1212,25 is not).
 
**CLOSEPOSITION COMMAND**
This command will close a position and requires an account name value and an instrument name value. The instrument name value is the name of the NinjaTrader instrument including the exchange name. For equities, the symbol is sufficient. This command will cancel any working orders and flatten the position.
 
**CLOSESTRATEGY COMMAND**
This command will close an ATM Strategy and requires a strategy ID value. The strategy ID value must match a strategy ID given to a strategy in the PLACE command. This command will close the specified strategy.

**FLATTENEVERYTHING COMMAND**
This command will cancel all active orders and flatten all positions across all accounts and broker connections.

**PLACE COMMAND**
This command will place orders, place orders that initiate a NinjaTrader ATM Strategy, or place orders that are applied to an active NinjaTrader position ATM Strategy. Providing the optional strategy name field with a valid ATM Strategy template name will result in execution of that ATM Strategy once the order is partially or completely filled. Pass in an optional unique string value for the strategy ID in order to reference that ATM Strategy via other commands. To apply an order to an active ATM Strategy (existing strategies Stop Loss and Profit Target orders are amended) pass in the active strategy ID value and leave the strategy name field blank. Pass in an optional unique string value for the order ID in order to reference that order via other commands. If specifying an ATM Strategy template name, there is no need to pass in an order ID as the strategy based orders can be referenced by their internally generated names such as TARGET1, STOP1 and so on.
 
**REVERSEPOSITION COMMAND**
This command will close the current position and place an order in the opposite direction. The field requirements are identical to the PLACE command.

## Initialization

If using the DLL based interface, it is important to understand how the ATI is initialized with respect to referencing account names. The ATI is initialized to the first account name used in the first calling function.

Some functions accept an account name as a parameter. In most if not all functions, these parameters can be left blank in which case the "Default" account will be used.  You can set the Default account by left mouse clicking on the Tools menu in the NinjaTrader Control Center and selecting the menu item Options, once in the Options window select the Automated trading interface category and select the account you want to use from the Default account menu.  If your default account is set to 'Sim101' and you call functions and leave the account parameter blank, the Sim101 account will be automatically used.
 
**Example:**
•Default account = Sim101

•A function call is made with "" empty string as the account name argument

•Sim101 account is automatically used

•Subsequent function calls must use empty string if you want to reference the Sim101 account

•If you call a function and pass in the argument "Sim101", invalid information will be returned


## File Interface

### File Interface Overview

The File interface is an easy way you can instruct NinjaTrader to place and manage orders. To use this interface, just create Order Instruction Files (OIFs) in "My Documents\<NinjaTrader Folder>\incoming" and when NinjaTrader sees the instructions they will be processed immediately. This interface allows you the flexibility to create order instructions to NinjaTrader from any application that allows you to create text files.

### Order Instruction Files (OIF)

OIFs must be written to the folder "My Documents\<NinjaTrader Folder>\incoming" and be named oif*.txt. You can simply send an oif.txt file however, it is suggested that you increment each OIF so that you end up with unique file names such as oif1.txt, oif2.txt, oif3.txt. The reason is that if you send a lot of OIFs in rapid succession, you do run the risk of file locking problems if you always use the same file name. This will result in a situation where your file is not processed.

Each file must also contain correctly formatted line(s) of parameters. You may stack the instruction lines so that each file contains as many instruction lines as you desire. The delimiter required is the semicolon and this section is a good reference for generating correctly formatted OIF.  Files are processed the instant they are written to the hard disk without delay.

Please reference the Commands and Valid Parameters section for detailed information on available commands and parameters.

**Warning:**  Move or directly write OIF files to the incoming folder. Copying OIF files to the incoming folder can cause file locking problems.

The following are examples of the required format for each of the available commands. Required fields are embraced by <> where optional fields are embraced by [].

**CANCEL COMMAND**
CANCEL;;;;;;;;;;<ORDER ID>;;[STRATEGY ID]

**CANCELALLORDERS COMMAND**
CANCELALLORDERS;;;;;;;;;;;;

**CHANGE COMMAND**
CHANGE;;;;<QUANTITY>;;<LIMIT PRICE>;<STOP PRICE>;;;<ORDER ID>;;[STRATEGY ID]

**CLOSEPOSITION COMMAND**
CLOSEPOSITION;<ACCOUNT>;<INSTRUMENT>;;;;;;;;;;

**CLOSESTRATEGY COMMAND**
CLOSESTRATEGY;;;;;;;;;;;;<STRATEGY ID>

**FLATTENEVERYTHING COMMAND**
FLATTENEVERYTHING;;;;;;;;;;;;

**PLACE COMMAND**
PLACE;<ACCOUNT>;<INSTRUMENT>;<ACTION>;<QTY>;<ORDER TYPE>;[LIMIT PRICE];[STOP PRICE];<TIF>;[OCO ID];[ORDER ID];[STRATEGY];[STRATEGY ID]
 
**REVERSEPOSITION COMMAND**
REVERSEPOSITION;<ACCOUNT>;<INSTRUMENT>;<ACTION>;<QTY>;<ORDER TYPE>;[LIMIT PRICE];[STOP PRICE];<TIF>;[OCO ID];[ORDER ID];[STRATEGY];[STRATEGY ID]

### Information Update Files

NinjaTrader provides update information files that are written to the folder "My Documents\<NinjaTrader Folder>\outgoing". The contents of this folder will be deleted when the NinjaTrader application is restarted.

#### Understanding order state files

**Order State Files**
Orders that are assigned an order ID value in the "PLACE" command will generate an order state update file with each change in order state. The file name is 'orderId.txt' where orderId is the order ID value passed in from the "PLACE" command. Possible order state values can be found here. The format of this file is:

**Order State;Filled Amount;Average FillPrice**


#### Understanding position update files

**Position Update Files**
Position update files are generated on every update of a position. The name of the file is Instrument Name + Instrument Exchange_AccountName_Position.txt. An example would be ES 0914 Globex_Sim101_Position.txt. The format of the file is:

**Market Position; Quantity; Average Entry Price**
Valid Market Position values are either LONG, SHORT or FLAT.

#### Understanding connection state files

**Connection State Files**
Connection state files are written with each change of connection state. The name of the file is ConnectionName.txt where connectionName is the name of the connection given in the Connection Manager. The format of the file is:

**Connection State**
Valid connection state values are CONNECTED or DISCONNECTED.

## DLL Interface

### DLL Functions Overview

The .net managed DLL Interface functions are contained in NTDirect.dll located in the C:\Program Files(X86)\NinjaTrader 8\bin\NinjaTrader.Client.DLL. OR YOUR SYSTEM'S EQUIVALENT.

### Functions
	
#### DLL Interface Functions

**int Ask(string instrument, double price, int size)**
Sets the ask price and size for the specified instrument. A return value of 0 indicates success and -1 indicates an error.

**int AskPlayback(string instrument, double price, int size, string timestamp)**
Sets the ask price and size for the specified instrument for use when synchronizing NinjaTrader playback with an external application playback. A return value of 0 indicates success and -1 indicates an error. The timestamp parameter format is "yyyyMMddHHmmss".

**double AvgEntryPrice(string instrument, string account)**
Gets the average entry price for the specified instrument/account combination.

**double AvgFillPrice(string orderId)**
Gets the average entry price for the specified orderId.

**int Bid(string instrument, double price, int size)**
Sets the bid price and size for the specified instrument. A return value of 0 indicates success and -1 indicates an error.

**int BidPlayback(string instrument, double price, int size, string timestamp)**
Sets the bid price and size for the specified instrument for use when synchronizing NinjaTrader playback with an external application playback. A return value of 0 indicates success and -1 indicates an error. The timestamp parameter format is "yyyyMMddHHmmss".

**double BuyingPower(string account)**
Gets the buying power for the specified account. *Not all brokerage technologies support this value.

**double CashValue(string account)**
Gets the cash value for the specified account. *Not all brokerage technologies support this value.

**int Command(string command, string account, string instrument, string action, int quantity, string orderType, double limitPrice, double stopPrice,
string timeInForce, string oco, string orderId, string strategy, string strategyId)**
Function for submitting, cancelling and changing orders, positions and strategies. Refer to the Commands and Valid Parameters section for detailed information. The Log tab will list context sensitive error information.

**int ConfirmOrders(int confirm)**
The parameter confirm indicates if an order confirmation message will appear. This toggles the global option that can be set manually in the NinjaTrader Control Center by selecting the Tools menu and the menu item Options, then checking the "Confirm order placement" checkbox. A value of 1 sets this option to true, any other value sets this option to false.

**int Connected(int showMessage)**
Returns a value of zero if the DLL has established a connection to the NinjaTrader server (application) and if the ATI is currently enabled or, -1 if it is disconnected. Calling any function in the DLL will automatically initiate a connection to the server. The parameter showMessage indicates if a message box is displayed in case the connection cannot be established. A value of 1 = show message box, any other value = don't show message box.

**int Filled(string orderId)**
Gets the number of contracts/shares filled for the orderId.

**int Last(string instrument, double price, int size)**
Sets the last price and size for the specified instrument. A return value of 0 indicates success and -1 indicates an error.

**int LastPlayback(string instrument, double price, int size, string timestamp)**
Sets the last price and size for the specified instrument for use when synchronizing NinjaTrader playback with an external application playback. A return value of 0 indicates success and -1 indicates an error. The timestamp parameter format is "yyyyMMddHHmmss".

**double MarketData(string instrument, int type)**
Gets the most recent price for the specified instrument and data type. 0 = last, 1 = bid, 2 = ask. You must first call the SubscribeMarketData() function prior to calling this function.

**int MarketPosition(string instrument, string account)**
Gets the market position for an instrument/account combination. Returns 0 for flat, negative value for short positive value for long.

**string NewOrderId()**
Gets a new unique order ID value.

**string Orders(string account)**
Gets a string of order ID's of all orders of an account separated by '|'. *If a user defined order ID was not originally provided, the internal token ID value is used since it is guaranteed to be unique.

**string OrderStatus(string orderId)**
Gets the order state (see definitions) for the orderId. Returns an empty string if the order ID value provided does not return an order.

**double RealizedPnL(string account)**
Gets the realized profit and loss of an account.

**int SetUp(string host, int port)**
Optional function to set the host and port number. By default, host is set to "localhost" and port is set to 36973. The default port number can be set via the General tab under Options. If you change these default values, this function must be called before any other function. A return value of 0 indicates success and -1 indicates an error.

**string StopOrders(string strategyId)**
Gets a string of order ID's of all Stop Loss orders of an ATM Strategy separated by '|'. Internal token ID value is used since it is guaranteed to be unique.

**string Strategies(string account)**
Gets a string of strategy ID's of all ATM Strategies of an account separated by '|'. Duplicate ID values can be returned if strategies were initiated outside of the ATI.

**int StrategyPosition(string strategyId)**
Gets the position for a strategy. Returns 0 for flat, negative value for short and positive value for long.

**int SubscribeMarketData(string instrument)**
Starts a market data stream for the specific instrument. Call the MarketData() function to retrieve prices. Make sure you call the UnSubscribeMarketData() function to close the data stream. A return value of 0 indicates success and -1 indicates an error.

**string TargetOrders(string strategyId)**
Gets a string of order ID's of all Profit Target orders of an ATM Strategy separated by '|'. Internal token ID value is used since it is guaranteed to be unique.

**int TearDown()**
Disconnects the DLL from the NinjaTrader server. A return value of 0 indicates success and -1 indicates an error.

**int UnsubscribeMarketData(string instrument)**
Stops a market data stream for the specific instrument. A return value of 0 indicates success and -1 indicates an error.