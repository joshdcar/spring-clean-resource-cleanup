using System;

namespace SpringClean{
    public class ExtendModel{

        public string ResourceGroupName {get;set;}
        public int ExtendHours {get;set;}
        public int ResponseExpirationHours {get;set;}
        public string ExpirationEmail {get;set;}
        public DateTime ExpirationDate {get;set;}
        public DateTime ResponseExpires {get;set;}
        public string InstanceId {get;set;}

    }
}

