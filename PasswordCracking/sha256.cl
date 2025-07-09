//Debug default
//#ifdef USE_DEBUG_DEFAULTS

    #define KEY_LENGTH 17
    #define SALT_LENGTH 6
    #define SALT_STRING Hf45DD

    //Key: 'enclosesHf45DD' (8:6)
    #define HASH_0 2627633883
    #define HASH_1 342531070
    #define HASH_2 1941191403
    #define HASH_3 4203335048
    #define HASH_4 2588921030
    #define HASH_5 4270942822
    #define HASH_6 3704920044
    #define HASH_7 3301016226

//#endif


//Base SHA-256 context
#define H0 0x6a09e667
#define H1 0xbb67ae85
#define H2 0x3c6ef372
#define H3 0xa54ff53a
#define H4 0x510e527f
#define H5 0x9b05688c
#define H6 0x1f83d9ab
#define H7 0x5be0cd19

//String convert macro
#define STR(s) #s
#define XSTR(s) STR(s)

//Methods
// << : bitshift left
// >> : bitshift right
// ^  : bitwise XOR
// ~  : bitwise NOT
// &  : bitwise AND
// |  : bitwise OR


inline uint rotr(uint x, int n) //Rotate right
{
    return (x >> n) | (x << (32 - n));
}
inline uint ch(uint x, uint y, uint z) //Choice based on x
{
    return (x & y) ^ (~x & z);
}
inline uint maj(uint x, uint y, uint z) //Majority of bits in x, y
{
    return (x & y) ^ (x & z) ^ (y & z);
}
inline uint sig0(uint x)
{
    return rotr(x, 7) ^ rotr(x, 18) ^ (x >> 3);
}
inline uint sig1(uint x)
{
    return rotr(x, 17) ^ rotr(x, 19) ^ (x >> 10);
}
inline uint csig0(uint x)
{
    return rotr(x, 2) ^ rotr(x, 13) ^ rotr(x, 22);
}
inline uint csig1(uint x)
{
    return rotr(x, 6) ^ rotr(x, 11) ^ rotr(x, 25);
}


// New kernel for GPU-based combination generation and hashing
kernel void generate_and_hash_kernel(
    global char* charset,           // Character set (e.g., "abcdefghijklmnopqrstuvwxyz")
    uint charset_length,            // Length of character set
    uint max_length,                // Maximum password length
    uint start_length,              // Starting password length
    global char* target_hash,       // Target hash to find (64 chars)
    global int* found_flag,         // Flag to signal when found
    global char* found_password,    // Output buffer for found password
    global ulong* total_combinations, // Total combinations to process
    global ulong* batch_offset,     // Offset for this batch
    global ulong* found_index       // Exact global work item index where password was found
)
{
    uint globalID = get_global_id(0);
    uint localID = get_local_id(0);
    
    // Calculate which combination this thread should generate
    // FIXED: Add batch offset to get the true combination index
    ulong combination_index = globalID + *batch_offset;
    
    // Check if we've exceeded total combinations
    if (combination_index >= *total_combinations) {
        return;
    }
    
    // Check if password already found by another thread
    if (*found_flag != 0) {
        return;
    }
    
    // Generate password combination based on globalID
    char password[32]; // Max 32 character password
    uint password_length = 0;
    
    // Convert combination_index to base-charset_length number system
    // to generate the specific combination for this thread
    ulong temp_index = combination_index;
    ulong length_offset = 0;
    uint current_length = start_length;
    
    // Find which length this combination belongs to
    while (current_length <= max_length) {
        ulong combinations_for_length = 1;
        for (uint i = 0; i < current_length; i++) {
            combinations_for_length *= charset_length;
        }
        
        if (temp_index < combinations_for_length) {
            password_length = current_length;
            break;
        }
        
        temp_index -= combinations_for_length;
        current_length++;
    }
    
    // If we couldn't find a valid length, return
    if (password_length == 0 || password_length > max_length) {
        return;
    }
    
    // Generate the specific combination for this length
    for (int i = password_length - 1; i >= 0; i--) {
        password[i] = charset[temp_index % charset_length];
        temp_index /= charset_length;
    }
    password[password_length] = '\0';
    
    // Now hash this password using the same SHA-256 logic
    int qua, mod;
    uint length = password_length;
    uint A, B, C, D, E, F, G, H;
    uint T1, T2;
    uint W[80];
    
    const uint K[64] = {
       0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5,
       0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3, 0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174,
       0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc, 0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da,
       0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7, 0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967,
       0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13, 0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85,
       0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3, 0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
       0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3,
       0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208, 0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2
    };
    
    // Reset algorithm
    #pragma unroll
    for (int i = 0; i < 80; i++) {
        W[i] = 0x00000000;
    }
    
    // Create message block
    qua = length / 4;
    mod = length % 4;
    for (int i = 0; i < qua; i++) {
        W[i]  = (password[i * 4 + 0]) << 24;
        W[i] |= (password[i * 4 + 1]) << 16;
        W[i] |= (password[i * 4 + 2]) << 8;
        W[i] |= (password[i * 4 + 3]);
    }
    
    // Pad remaining uint
    if (mod == 0) {
        W[qua] = 0x80000000;
    } else if (mod == 1) {
        W[qua] = (password[qua * 4]) << 24;
        W[qua] |= 0x800000;
    } else if (mod == 2) {
        W[qua] = (password[qua * 4]) << 24;
        W[qua] |= (password[qua * 4 + 1]) << 16;
        W[qua] |= 0x8000;
    } else {
        W[qua] = (password[qua * 4]) << 24;
        W[qua] |= (password[qua * 4 + 1]) << 16;
        W[qua] |= (password[qua * 4 + 2]) << 8;
        W[qua] |= 0x80;
    }
    
    W[15] = length * 8; // Add key length
    
    // Run message schedule
    #pragma unroll
    for (int i = 16; i < 64; i++) {
        W[i] = sig1(W[i - 2]) + W[i - 7] + sig0(W[i - 15]) + W[i - 16];
    }
    
    // Prepare compression
    A = H0; B = H1; C = H2; D = H3;
    E = H4; F = H5; G = H6; H = H7;
    
    // Compress
    #pragma unroll
    for (int i = 0; i < 64; i++) {
        T1 = H + csig1(E) + ch(E, F, G) + K[i] + W[i];
        T2 = csig0(A) + maj(A, B, C);
        H = G; G = F; F = E; E = D + T1;
        D = C; C = B; B = A; A = T1 + T2;
    }
    
    // Add the compressed chunk's hash to the initial hash value
    uint h0 = H0 + A;
    uint h1 = H1 + B;
    uint h2 = H2 + C;
    uint h3 = H3 + D;
    uint h4 = H4 + E;
    uint h5 = H5 + F;
    uint h6 = H6 + G;
    uint h7 = H7 + H;
    
    // Convert to hex string
    char computed_hash[65];
    char hex_charset[] = "0123456789abcdef";
    uint hash_values[8] = {h0, h1, h2, h3, h4, h5, h6, h7};
    
    #pragma unroll
    for (int j = 0; j < 8; j++) {
        uint currentVal = hash_values[j];
        #pragma unroll
        for (int len = 8 - 1; len >= 0; currentVal >>= 4, --len) {
            computed_hash[(j * 8) + len] = hex_charset[currentVal & 0xf];
        }
    }
    computed_hash[64] = '\0';
    
    // Compare with target hash
    bool match = true;
    #pragma unroll
    for (int i = 0; i < 64; i++) {
        if (computed_hash[i] != target_hash[i]) {
            match = false;
            break;
        }
    }
    
    // If match found, set flag and copy password
    if (match) {
        // Atomically set found flag - first thread to find password wins
        int was_found_before = atomic_cmpxchg(found_flag, 0, 1);
        
        // Only the first thread to find the password records the details
        if (was_found_before == 0) {
            // Store the exact global work item ID where password was found
            *found_index = globalID;
            
            // Copy found password
            #pragma unroll
            for (int i = 0; i < password_length && i < 31; i++) {
                found_password[i] = password[i];
            }
            found_password[password_length] = '\0';
        }
    }
}