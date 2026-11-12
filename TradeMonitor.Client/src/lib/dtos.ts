/* Options:
Date: 2026-11-12 02:06:30
Version: 10.50
Tip: To override a DTO option, remove "//" prefix before updating
BaseUrl: https://localhost:5051

//GlobalNamespace: 
//MakePropertiesOptional: False
//AddServiceStackTypes: True
//AddResponseStatus: False
//AddImplicitVersion: 
//AddDescriptionAsComments: True
//IncludeTypes: 
//ExcludeTypes: 
//DefaultImports: 
*/

// @ts-nocheck

export interface IReturn<T>
{
    createResponse(): T;
}

export interface IReturnVoid
{
    createResponse(): void;
}

export interface IHasSessionId
{
    sessionId?: string;
}

export interface IHasBearerToken
{
    bearerToken?: string;
}

export interface IGet
{
}

export interface IPost
{
}

// @DataContract
export class QueryBase
{
    // @DataMember(Order=1)
    public skip?: number;

    // @DataMember(Order=2)
    public take?: number;

    // @DataMember(Order=3)
    public orderBy?: string;

    // @DataMember(Order=4)
    public orderByDesc?: string;

    // @DataMember(Order=5)
    public include?: string;

    // @DataMember(Order=6)
    public fields?: string;

    // @DataMember(Order=7)
    public meta?: { [index:string]: string; };

    public constructor(init?: Partial<QueryBase>) { (Object as any).assign(this, init); }
}

export class QueryDb<T> extends QueryBase
{

    public constructor(init?: Partial<QueryDb<T>>) { super(init); (Object as any).assign(this, init); }
}

export class User
{
    public id: string;
    public userName: string;
    public firstName?: string;
    public lastName?: string;
    public displayName?: string;
    public profileUrl?: string;

    public constructor(init?: Partial<User>) { (Object as any).assign(this, init); }
}

// @DataContract
export class ResponseError
{
    // @DataMember(Order=1)
    public errorCode: string;

    // @DataMember(Order=2)
    public fieldName: string;

    // @DataMember(Order=3)
    public message: string;

    // @DataMember(Order=4)
    public meta?: { [index:string]: string; };

    public constructor(init?: Partial<ResponseError>) { (Object as any).assign(this, init); }
}

// @DataContract
export class ResponseStatus
{
    // @DataMember(Order=1)
    public errorCode: string;

    // @DataMember(Order=2)
    public message?: string;

    // @DataMember(Order=3)
    public stackTrace?: string;

    // @DataMember(Order=4)
    public errors?: ResponseError[];

    // @DataMember(Order=5)
    public meta?: { [index:string]: string; };

    public constructor(init?: Partial<ResponseStatus>) { (Object as any).assign(this, init); }
}

// @DataContract
export class QueryResponse<T>
{
    // @DataMember(Order=1)
    public offset: number;

    // @DataMember(Order=2)
    public total: number;

    // @DataMember(Order=3)
    public results: T[] = [];

    // @DataMember(Order=4)
    public meta?: { [index:string]: string; };

    // @DataMember(Order=5)
    public responseStatus?: ResponseStatus;

    public constructor(init?: Partial<QueryResponse<T>>) { (Object as any).assign(this, init); }
}

export class HelloResponse
{
    public result: string;

    public constructor(init?: Partial<HelloResponse>) { (Object as any).assign(this, init); }
}

// @DataContract
export class RegisterResponse implements IHasSessionId, IHasBearerToken
{
    // @DataMember(Order=1)
    public userId?: string;

    // @DataMember(Order=2)
    public sessionId?: string;

    // @DataMember(Order=3)
    public userName?: string;

    // @DataMember(Order=4)
    public referrerUrl?: string;

    // @DataMember(Order=5)
    public bearerToken?: string;

    // @DataMember(Order=6)
    public refreshToken?: string;

    // @DataMember(Order=7)
    public refreshTokenExpiry?: string;

    // @DataMember(Order=8)
    public roles?: string[];

    // @DataMember(Order=9)
    public permissions?: string[];

    // @DataMember(Order=10)
    public redirectUrl?: string;

    // @DataMember(Order=11)
    public responseStatus?: ResponseStatus;

    // @DataMember(Order=12)
    public meta?: { [index:string]: string; };

    public constructor(init?: Partial<RegisterResponse>) { (Object as any).assign(this, init); }
}

// @DataContract
export class AuthenticateResponse implements IHasSessionId, IHasBearerToken
{
    // @DataMember(Order=1)
    public userId?: string;

    // @DataMember(Order=2)
    public sessionId?: string;

    // @DataMember(Order=3)
    public userName?: string;

    // @DataMember(Order=4)
    public displayName?: string;

    // @DataMember(Order=5)
    public referrerUrl?: string;

    // @DataMember(Order=6)
    public bearerToken?: string;

    // @DataMember(Order=7)
    public refreshToken?: string;

    // @DataMember(Order=8)
    public refreshTokenExpiry?: string;

    // @DataMember(Order=9)
    public profileUrl?: string;

    // @DataMember(Order=10)
    public roles?: string[];

    // @DataMember(Order=11)
    public permissions?: string[];

    // @DataMember(Order=12)
    public authProvider?: string;

    // @DataMember(Order=13)
    public responseStatus?: ResponseStatus;

    // @DataMember(Order=14)
    public meta?: { [index:string]: string; };

    public constructor(init?: Partial<AuthenticateResponse>) { (Object as any).assign(this, init); }
}

// @Route("/hello/{Name}")
export class Hello implements IReturn<HelloResponse>, IGet
{
    public name: string;

    public constructor(init?: Partial<Hello>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'Hello'; }
    public getMethod() { return 'GET'; }
    public createResponse() { return new HelloResponse(); }
}

/** @description Sign Up */
// @Api(Description="Sign Up")
// @DataContract
export class Register implements IReturn<RegisterResponse>, IPost
{
    // @DataMember(Order=1)
    public userName?: string;

    // @DataMember(Order=2)
    public firstName?: string;

    // @DataMember(Order=3)
    public lastName?: string;

    // @DataMember(Order=4)
    public displayName?: string;

    // @DataMember(Order=5)
    public email?: string;

    // @DataMember(Order=6)
    public password?: string;

    // @DataMember(Order=7)
    public confirmPassword?: string;

    // @DataMember(Order=8)
    public autoLogin?: boolean;

    // @DataMember(Order=10)
    public errorView?: string;

    // @DataMember(Order=11)
    public meta?: { [index:string]: string; };

    public constructor(init?: Partial<Register>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'Register'; }
    public getMethod() { return 'POST'; }
    public createResponse() { return new RegisterResponse(); }
}

// @Route("/confirm-email")
export class ConfirmEmail implements IReturnVoid, IGet
{
    public userId: string;
    public code: string;
    public returnUrl?: string;

    public constructor(init?: Partial<ConfirmEmail>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'ConfirmEmail'; }
    public getMethod() { return 'GET'; }
    public createResponse() {}
}

/** @description Sign In */
// @Route("/auth", "GET,POST")
// @Route("/auth/{provider}", "POST")
// @Api(Description="Sign In")
// @DataContract
export class Authenticate implements IReturn<AuthenticateResponse>, IPost
{
    /** @description AuthProvider, e.g. credentials */
    // @DataMember(Order=1)
    public provider?: string;

    // @DataMember(Order=2)
    public userName?: string;

    // @DataMember(Order=3)
    public password?: string;

    // @DataMember(Order=4)
    public rememberMe?: boolean;

    // @DataMember(Order=5)
    public accessToken?: string;

    // @DataMember(Order=6)
    public accessTokenSecret?: string;

    // @DataMember(Order=7)
    public returnUrl?: string;

    // @DataMember(Order=8)
    public errorView?: string;

    // @DataMember(Order=9)
    public meta?: { [index:string]: string; };

    public constructor(init?: Partial<Authenticate>) { (Object as any).assign(this, init); }
    public getTypeName() { return 'Authenticate'; }
    public getMethod() { return 'POST'; }
    public createResponse() { return new AuthenticateResponse(); }
}

// @ValidateRequest(Validator="IsAdmin")
export class QueryUsers extends QueryDb<User> implements IReturn<QueryResponse<User>>
{
    public id?: string;

    public constructor(init?: Partial<QueryUsers>) { super(init); (Object as any).assign(this, init); }
    public getTypeName() { return 'QueryUsers'; }
    public getMethod() { return 'GET'; }
    public createResponse() { return new QueryResponse<User>(); }
}

